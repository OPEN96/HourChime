# 贡献指南

感谢你愿意改进 HourChime。这是一个纯本地、无联网、零第三方依赖的小工具，请在提改动时保持这个定位。

## 开发环境

| 项目 | 要求 |
| --- | --- |
| 操作系统 | Windows 10 1809+ / Windows 11，x64（WPF + WinForms 混合，无法跨平台构建） |
| SDK | [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) |
| IDE | Visual Studio 2022（.NET 桌面开发工作负载）或 VS Code + C# Dev Kit |
| Shell | PowerShell 5.1+ 或 PowerShell 7（`scripts/build.ps1` 两者都兼容） |

```powershell
git clone https://github.com/OPEN96/HourChime.git
cd HourChime
dotnet restore HourChime.sln
dotnet build HourChime.sln -c Release
dotnet run --project HourChimeWpf/HourChimeWpf.csproj
```

## 代码结构

| 路径 | 职责 |
| --- | --- |
| `HourChimeWpf/Core/AppSettings.cs` | 配置模型、JSON 持久化、所有中文播报文案（`Texts`） |
| `HourChimeWpf/Core/ChimeScheduler.cs` | 报时、闹钟、贪睡、重复提醒、免打扰、暂停的全部调度逻辑 |
| `HourChimeWpf/Core/SpeechService.cs` | SAPI 语音、提示音 / 自定义 WAV 播放 |
| `HourChimeWpf/Core/Platform.cs` | 开机自启、DWM 圆角、配置目录 |
| `HourChimeWpf/Core/CrashLog.cs` | 崩溃日志 |
| `HourChimeWpf/TrayHost.cs` | `NotifyIcon` 与右键菜单结构、状态同步 |
| `HourChimeWpf/Styles/` | WPF 主题与托盘菜单深色渲染器 |
| `HourChimeWpf/Views/` | 主窗口、闹钟编辑、Toast、确认框 |
| `HourChimeWpf/Program.cs` / `App.xaml.cs` | 显式入口、资源合并、Toast 事件接线 |
| `scripts/build.ps1` | 本地与 CI 共用的打包脚本 |

## 开发约定

- **新增配置字段必须带默认值**，保证旧版 `settings.json` 直接可用，不需要迁移脚本。
- **调度逻辑只放在 `ChimeScheduler`**，界面通过 `Tick` / `StateChanged` / `AlarmsChanged` / `ToastRequested` 事件订阅，不要在窗口代码里写计时器。
- 所有调度运行在 UI 线程的 `DispatcherTimer`（500 ms）上，不要引入后台线程直接操作 UI。
- 中文播报文案集中在 `Texts`；界面文案在 XAML 中，保持简体中文。
- 托盘菜单改动请用 `/traypreview:<png>` 自截图核对，**不要**给 `ContextMenuStrip` 设置 `RenderMode`（会覆盖自定义渲染器），也**不要**关掉 `ShowImageMargin` / `ShowCheckMargin`（勾号会消失）。
- 不引入第三方 NuGet 包，不引入联网、账号、遥测或云端服务。
- 提交信息建议遵循 [Conventional Commits](https://www.conventionalcommits.org/zh-hans/v1.0.0/)：`feat:` / `fix:` / `docs:` / `chore:` / `refactor:`。

## 调试技巧

- 配置在 `%APPDATA%\HourChime\settings.json`。改它之前先备份；应用只在设置变更时写回。
- 异常记录在 `%APPDATA%\HourChime\crash.log`；正常情况下这个文件不应新增内容。
- 应用使用名为 `HourChime.SingleInstance` 的互斥体，重复启动只会弹提示。调试前先结束已有进程。
- 常用启动参数：`/minimized`、`/previewtoast`、`/traypreview[:<png>]`、`/snapshot:<png>`。完整说明见 [README](README.md#调试参数)。
- 截主窗口请用 `/snapshot:<png>`（应用内 `RenderTargetBitmap` 自渲染）；`PrintWindow` 抓 WPF 窗口常得到全黑图。
- 构建时有 2 个已知无害警告（`SpeechService.cs` 空引用静态分析、`app.manifest` 高 DPI 提示），不必为此改动，但也请不要新增警告。

## 提交 PR

1. 从 `main` 建分支，改动尽量聚焦单一主题。
2. 本地执行 `dotnet build HourChime.sln -c Release`，并在中文 Windows 上实际运行验证。
3. 涉及界面或文案时，同步更新 `CHANGELOG.md` 的 `[Unreleased]` 段落与 README 截图（若变化明显）。
4. 按 PR 模板完成自查，特别确认没有提交 `bin/`、`obj/`、`artifacts/`、个人 `settings.json` 或 `HourChimeWpf/shots/` 下的调试截图。
5. 在 PR 描述里关联 Issue。

## 报告问题

请使用 [Issue 模板](https://github.com/OPEN96/HourChime/issues/new/choose)，附上版本、使用的发布包、Windows 版本与 `crash.log`（如有）。请勿粘贴 `settings.json` 全文。
