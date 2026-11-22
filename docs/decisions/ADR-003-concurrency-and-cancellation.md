# ADR-003 — 并发与取消模型

- 状态：**Accepted**（2026-11-22 人类裁决，见 [RULINGS-2026-11-22.md](RULINGS-2026-11-22.md)）
- 日期：2026-11-22
- 影响范围：协议层、传输层、状态层、存储层

## 1. 背景

流式请求的生命周期横跨网络、解析、持久化、UI 渲染四层。必须定义：谁能取消、取消后已生成内容归谁、重连语义、并发请求隔离。双端语义必须一致，否则 M1 的"逐字节一致"判据失效。

## 2. 生命周期（双端同构）

```
Idle → Preparing → Connecting → Streaming → (Finalizing | Cancelling | Failing) → Terminal
```

| 状态 | 进入条件 | 允许的转换 | 必须做的清理 |
| --- | --- | --- | --- |
| Preparing | 用户发送 / 重新生成 | Connecting, Failing(本地校验失败) | — |
| Connecting | 请求已构造 | Streaming(首个字节), Cancelling, Failing | — |
| Streaming | 首个传输帧到达 | Finalizing, Cancelling, Failing | — |
| Finalizing | 收到 done 或 finish_reason | Terminal | 落盘最终 `message_part`、写 `usage_record` |
| Cancelling | 用户点停止 / 作用域销毁 | Terminal | **必须关闭 socket**（不只是停止读取）、保留已生成内容 |
| Failing | 不可恢复错误 | Terminal | 落盘部分内容 + 错误对象 + `usage_record`（若有） |
| Terminal | — | 不可再变 | 释放请求作用域 |

**关键约束**：

1. 取消必须传播到 socket 层。Android：`Flow` 的 `collect` 作用域取消 → OkHttp `Call.cancel()`（`CancellationException` 不得被 `catch (e: Exception)` 吞掉，必须以 `ensureActive()` 或显式 rethrow 处理）。Windows：`CancellationToken` 必须一路传到 `HttpClient.SendAsync(..., ct)` 与 `ReadAsStreamAsync(ct)`，且 `OperationCanceledException` 必须与超时异常区分（`ex.CancellationToken == ct` 判据）。
2. 每个会话同一时刻最多一个活跃流。新发送若检测到活跃流 → 先取消旧的并**等待其进入 Terminal**（≤2s），再开始新的。禁止两个流写同一个 `message`。
3. 已生成内容必须保留：取消时把已收到的 `message_part` 标记 `truncated=true` 并落盘，UI 显示"已停止"而不是删除。
4. 用户取消 → 流以 `done{finish_reason:"aborted_user"}` 结束（即使没有 delta），不产生 `failed`。超时与网络错误 → `failed`。
5. 页面/窗口销毁 → 作用域取消 → 走 Cancelling 路径，落盘已完成部分。**流式仅前台保证**（人类裁决 2026-11-22）：后台挂起是合法行为（Android 后台被杀、Windows 现代待机同理），不视为缺陷，也不需要“补救”。
6. **回前台“继续生成”**（同一裁决）：若某条消息因挂起而中断，UI 必须在**两处**明示——
   - (a) 消息气泡内的中断标记行（“已中断 · 后台挂起”）；
   - (b) 输入框上方的常驻提示条，带“继续”按钮。
   点击“继续”后，以已生成内容为上下文**发起一次新的助手请求**，结果追加为新的 `message_part`，原消息保持 `status='aborted'` 不变。
   - 这**不是** provider 级断点续传（OpenAI / Anthropic / Gemini / OpenRouter 均无此接口），文案必须写“继续生成”，不得写“恢复连接”或“续传”。
   - **前台服务 + 常驻通知列为 v2，本期不做半个**：不做“有前台服务但不常驻通知”，也不做“有通知但流仍会断”。
7. **悬挂流修复**：进程被杀会留下 `status='streaming'` 的消息。应用启动时 MUST 执行一次修复：把超过 60s 未更新的 `streaming` 消息改为 `aborted`，保留已落盘 part，`finish_reason` 置 null，并在末尾追加一条 `notice` part 说明中断。MUST NOT 自动重发（自动重发会让用户为不可见的重试付费）。

## 3. 重连与重试

| 场景 | 语义 | 判据 |
| --- | --- | --- |
| 连接失败 / 首字节前超时 | 自动重试，最多 `max_retries`（默认 2），退避 0.5s / 1.5s + ±20% 抖动，尊重 `Retry-After`（上限 30s） | 尚未向 UI 发出任何 StreamEvent |
| 首字节后断开 | **不透明重试**。产生 `failed(NET_CONNECTION_RESET)`，带 `retry` 动作；用户点击后以同一参数快照发起**新消息**（原文保留，新内容追加为新的 `message_part`） | 已有 delta 上屏 |
| 后台挂起后回前台 | 保留中断消息，UI 提示可“继续生成”；点击后发起**客户端续写**（新消息），不复用原连接 | 原消息 `status='aborted'`，无 `failed` |
| 悬挂流（进程被杀） | 启动时修复为 `aborted` + `notice` part，不自动重发 | 修复后无 `streaming` 残留 |
| 服务端支持断点续传 | v1 **不实现**（OpenAI/Anthropic 均无可用续传）。接口预留：`StreamAttempt.resumable: Boolean` 恒 false | — |
| 重复发送 | 幂等键 `client_request_id`（UUIDv7）随请求头发出（若 provider 支持），本地用于去重 | — |

重试必须使用**参数快照**：`ChatRequest` 在 Preparing 阶段序列化为不可变快照，重试用快照而非当前设置（用户中途改温度不影响在途重试）。

## 4. 并发上限

- 全局默认 4 个活跃流（可在 provider 级调整）。
- 单 provider 独立信号量 + 单 Key 独立限流桶（M2 多 Key 时隔离）。
- 工具执行与流式并行：工具调用期间流仍处于 Streaming（等待工具结果），不占用额外并发槽。

## 5. 双端等价的可验证锚点

conformance runner 除了比对事件序列，还必须比对**取消行为**：

| 用例 | Android 断言 | Windows 断言 |
| --- | --- | --- |
| 流中途取消 | socket 已关闭（OkHttp 拦截器计数）、事件以 `aborted_user` 结束、部分内容落盘 1 行 | 同上，`CancellationToken.IsCancellationRequested` 为 true 且 `HttpRequestException` 未被误报为网络错误 |
| 取消后立刻重发 | 旧流进入 Terminal 后才发起新流，无交错写入 | 同 |
| 连接超时 | 触发自动重试且 UI 无事件 | 同 |
| 首字节后断开 | 不重试，发出 failed | 同 |

## 6. 后果

**负面**：
- "取消必须等 Terminal 再发新请求"会带来最多 2s 的感知延迟（用户连点发送）。接受，因为交错写入的代价是数据损坏。
- 不支持续传，长回复断线后需重新生成，用户可见体验损失，必须在错误文案里说明。

## 7. 复审触发条件

- 任一 provider 提供公开的流式续传接口
- 出现一次交错写入或双重落盘缺陷
- 自动重试被观测到对计费产生重复扣费
