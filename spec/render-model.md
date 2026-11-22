# spec/render-model.md — RenderModel（渲染 AST）

- 版本：**1.0.0-draft.3**
- 状态：DRAFT（待独立评审）
- 起草日期：2026-11-22
- 适用：双端 Markdown 解析器、渲染器、流式渲染、截图比对

**为什么要有这一层**：双端各自把 Markdown 解析成同一个 `RenderModel`，再各自渲染。没有这一层，"视觉等价"无法判定，只能靠人眼看图，而人眼不能进 CI。

解析器（Markdown → RenderModel）双端各写一份，但输出 MUST 逐字段相同；渲染器（RenderModel → 像素）双端各写，判据见 ADR-001 §5 的三层等价。

---

## 1. 信封

```json
{
  "v": 1,
  "blocks": [ /* Block[] */ ],
  "flags": {   // 闭集，additionalProperties 等价于 false：不得自增字段
    "incomplete": false,
    "has_math": true,
    "has_mermaid": false,
    "has_table": true,
    "has_code": false,
    "has_list": false,
    "has_footnote": false,
    "max_depth": 3,
    "truncated": false
  }
}
```

- `incomplete=true`：流仍在进行（最后一个块可能还会增长）。
- `truncated=true`：超过解析上限（§5），尾部被丢弃，UI MUST 提示"内容过长，尾部未渲染"。

## 2. Block（闭集，共 13 种）

| `type` | 字段 | 说明 |
| --- | --- | --- |
| `paragraph` | `inlines: Inline[]` | 普通段落 |
| `heading` | `level: 1..6`, `inlines` | ATX 与 Setext 均归入此类型 |
| `code` | `lang: string\|null`, `text: string`, `closed: bool`, `meta: {filename?:string, line_numbers?:bool}` | 围栏代码块与缩进代码块。`meta` 只从围栏 info string 解析，语法见 §2.1 |
| `math` | `tex: string`, `closed: bool`, `display: "block"` | `$$...$$` 或 `\\[...\\]` |
| `mermaid` | `src: string`, `closed: bool`, `kind: "flowchart"\|"sequence"\|"other"` | 围栏代码块。**只渲染 flowchart 与 sequence，静态、无动画**（人类裁决 2026-11-22）；`kind="other"` 一律降级为 `code` 等宽显示并明示 |
| `table` | `align: (\"left\"\|\"center\"\|\"right\"\|null)[]`, `header: Inline[][]`, `rows: Inline[][][]` | GFM 表格 |
| `list` | `ordered: bool`, `start: int`, `tight: bool`, `items: {checked: bool\|null, blocks: Block[]}[]` | 支持任务列表（`checked` 非 null 时） |
| `quote` | `blocks: Block[]` | 引用块，可嵌套 |
| `hr` | — | 分隔线 |
| `html` | `source: string` | **原始 HTML 一律转义为字面文本**，等宽渲染。不解析、不执行 |
| `footnote_def` | `label: string`, `blocks: Block[]` | 脚注定义 |
| `image` | `src: string`, `alt: string`, `title: string\|null` | 块级图片。判定条件见 §2.2，**不是**"看起来独占一行" |
| `unknown` | `kind: string`, `raw: string` | 前向兼容：遇到 v1 未定义的扩展语法时，原样保留并等宽渲染 |

### 2.1 代码围栏 info string 语法

\`meta\` 的解析规则（双端一致，否则 L1 必挂）：

\`\`\`
infostring := lang (SP attr)*
attr       := "filename=" value | "ln" | "line_numbers"
value      := 非空白连续串（含 / . - 时不需要引号），或用 "..." 包裹
\`\`\`

- 第一个 token 是 \`lang\`（可为空）。
- \`ln\` 或 \`line_numbers\` 出现 → \`meta.line_numbers = true\`。
- \`filename=xxx\` → \`meta.filename = "xxx"\`；识别不出的一律忽略（不报错、不降级）。
- 无法解析的 info string：整个 info string 当作 \`lang\` 原样保留，\`meta\` 为空对象。

### 2.2 图片：成块还是成行内

判定是确定性的，不允许"看起来独占一行"：

- 若一个段落的 **inlines 恰好只有一个** `image` 且该段落只有这一行源码（前后无其他 inline、无硬换行）→ 产出 Block \`image\`。
- 其余所有情况 → Inline \`image\`，外层包 \`paragraph\`。
- 判定只看解析后的 inline 序列长度，不看原始行内空格或缩进（行首缩进 1~3 空格不影响判定）。

### 2.3 引用式链接与链接定义（v1 不解析）

v1 **不实现**引用式链接：\`[文字][label]\` 与 \`[label]\` 原样保留为 \`text\`（保留方括号），\`[label]: url\` 单独一行归入 \`unknown{kind:"link_definition"}\`。

理由与 §4.5 的前缀稳定性直接相关：如果支持引用式链接，后到的定义会**回填修改前面已完成块**的 inline 内容，违反"除最后一块外不得变化"。这是刻意放弃的语法，写入 \`docs/PROJECT_STATE.md\` 的技术债表。

## 3. Inline（闭集，共 10 种）

| `type` | 字段 |
| --- | --- |
| `text` | `text: string` |
| `emph` | `inlines` |
| `strong` | `inlines` |
| `strike` | `inlines` |
| `code` | `text: string` |
| `link` | `dest: string`, `title: string\|null`, `inlines` |
| `image` | `src: string`, `alt: string`, `title: string\|null` |
| `math` | `tex: string`（行内 `$...$` / `\\(...\\)`，`display:"inline"` 隐含） |
| `footnote_ref` | `label: string` |
| `br` | — （硬换行） |

**内联嵌套上限 8 层**，超过则余下部分降级为 `text`（保留原始标记字符），防解析器退化。

## 4. 流式规则（防闪烁的核心契约）

1. **永不抛异常**：任何输入（含畸形、超长、未闭合、非法 UTF-8 已替换）都 MUST 产出 RenderModel。失败即降级为 `unknown` 或 `code{closed:false}`。
2. **不闭合围栏**：`\`\`\`lang` 未闭合 → `code{closed:false}`，按代码块渲染（含高亮，高亮器必须容忍不完整 token）。
3. **不闭合数学**：`$$` 未闭合 → `math{closed:false}`；渲染器 MUST 以等宽字面渲染直到 `closed=true`，一旦闭合再**原位切换**为排版渲染（同一个块 id，不重建视图）。
4. **不闭合 Mermaid**：`mermaid{closed:false}` 渲染为代码块；`closed=true` 后触发布局（异步，见 ADR-001 §2.4）。布局未完成前显示上一次的代码块形态，禁止空白闪烁。
5. **前缀稳定性（可判定）**：给定任意输入 `s` 与其前缀 `p`（`p` 是 `s` 的前缀），`parse(p).blocks` 除**最后一块**外，MUST 与 `parse(s).blocks` 的对应前缀逐字段相同。这条规则直接禁止"整篇重排导致的跳动"。conformance 用例 RENDER-STREAM-*。
6. **块 id**：解析器 MUST 为每块分配稳定 `id`（按块序号 `b0,b1,...`），渲染器据此复用视图（Android: `key(id)`；Windows: `ItemsRepeater` 的 `ElementFactory` 按 key 复用）。
7. **增量成本**：追加 N 个字符的处理时间 MUST 只与"最后一个开放块"相关，不得全篇重解析。实现方式：保留块边界与未完成块的起始偏移，只重解析尾部。判据唯一来源是 §6 的"末尾追加 10 字符重解析 ≤2ms（卓越）/≤4ms（地板）"。

## 5. 上限与截断

| 限制 | 值 | 超限行为 |
| --- | --- | --- |
| 总输入字符 | 200,000 | 截断，`truncated=true`，UI 提示 |
| 单消息块数 | 5,000 | 截断 |
| 表格列数 | 60 | 超出部分列丢弃 |
| 表格行数 | 5,000 | 截断 |
| 嵌套深度（list/quote） | 32 | 超出部分降级为 `unknown{kind:"list"\|"quote", raw:<原始标记>}`（`text` 不是 Block 类型，不得用它） |
| 内联嵌套 | 8 | 降级为 `text` |
| 代码块行数 | 20,000 | 超出部分不渲染但保留 `text`（折叠显示） |
| Mermaid 节点 / 边 | 200 / 2000 | 降级代码块 + 提示 |
| 解析总耗时 | 无硬上限（受上述限制约束） | — |

## 6. 性能目标（解析层）

| 指标 | 卓越 | 地板 |
| --- | --- | --- |
| 10 万字符首次解析（后台线程） | ≤60ms | ≤120ms |
| 末尾追加 10 字符重解析（§4.7 的唯一判据） | ≤2ms | ≤4ms |
| 20 组基准用例全量解析 | ≤40ms | ≤80ms |
| 内存占用（10 万字符 → 模型） | ≤8MB | ≤15MB |

这些数字由 `bench/common/render-parse` 测量，双端各跑，日志入 `docs/benchmarks/`。

## 7. 安全

- 原始 HTML MUST 转义为字面文本（`html` 块），不执行、不注入。
- 链接协议白名单：`http`, `https`, `mailto`。其他（`javascript:`, `data:`, `file:`, 自定义 scheme）MUST 渲染为不可点击的文本并在 UI 提示。
- 图片 `src` 只允许 `http(s)` 与本地附件引用（`attachment://<id>`）；远程图片默认不自动加载（隐私），用户可开启"加载远程图片"。
- 表格/列表的嵌套放大攻击（指数展开）由 §5 的深度与块数上限阻断。

## 8. 金样本与等价性

- `bench/render-goldens/<case>/input.md` + `.model.json`（20 组，M3 产出）。
- CI 检查：双端解析器对每个金样本的输出与 `.model.json` **逐字段相同**（L1 等价）。
- 结构等价（L2）：双端渲染器导出 `render-manifest.json`（块 type 序列 + 每块行数 + 文本 SHA-256 + 图片尺寸），逐字段相同（0 差异）。
- 视觉等价容差（人类裁决 2026-11-22）：块边界与文本基线位置偏差 **Android ≤2dp / Windows ≤2px**；文本内容逐字符一致；字体度量、抗锯齿、亚像素差异不计。
- 行为等价（L4）：同一份交互脚本在双端各跑一次（复制代码块、长按菜单、折叠/展开、链接点击、表格横滚、图片查看、引用跳转、分支切换、搜索跳转、脚注跳转、Mermaid 降级复制、任务列表只读勾选），全部通过。
- 视觉等价（L3）：截图比对，容差按 ADR-001 §5。

## 9. 变更记录

| 版本 | 日期 | 变更 |
| --- | --- | --- |
| 1.0.0-draft | 2026-11-22 | 首版 |
