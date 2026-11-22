# 本机工具链（2026-11-23 实测）

| 组件 | 状态 | 位置/版本 | 备注 |
| --- | --- | --- | --- |
| .NET SDK 9 | ✅ 用户态安装 | `C:\Users\linch\dotnet9\sdk`（9.0.321 + 9.0.23 运行时） | 系统只有 .NET 8，`net9.0` 工程会报 NETSDK1045；所有 dotnet 命令用 `~/dotnet9/sdk/dotnet.exe` |
| Java | ✅ 21 | 系统 | Gradle 用 |
| Gradle | ⚠️ 未装 wrapper | 系统 gradle 可用 | wrapper jar 待补（QE-001） |
| Android SDK | ❌ 缺 | — | 需要 cmdline-tools + platform 34 + build-tools；缺则 Android 构建跳过 |
| Windows SDK / WinUI | 待确认 | — | WinUI 3 工程需要 Windows App SDK |
| Python 3 | ✅ | 系统 | 跑 spec-lint / conformance 工具 |
| git | ✅ 2.53 | — | github.com:443 不通，推送走 `api.github.com`（`.git/push_api.py`） |

## 约定

- 所有 .NET 命令显式用 `~/dotnet9/sdk/dotnet.exe`，不要依赖 PATH。
- 推送只走 `python3 .git/push_api.py "<message>"`，需要 `GH_TOKEN`。
- 提交按路径 add，禁止 `git add -A`（多线程共享工作树）。
