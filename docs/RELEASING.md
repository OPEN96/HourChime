# 发布流程

发布由 [`.github/workflows/build.yml`](../.github/workflows/build.yml) 自动化完成，本地只需准备版本号与变更记录。

## 版本号从哪来

| 场景 | 版本来源 |
| --- | --- |
| 本地 `dotnet build` | `HourChimeWpf/HourChimeWpf.csproj` 的 `<Version>` |
| CI 常规构建（push / PR / 手动） | `0.0.0-ci.<run_number>`，或手动触发时填写的 `version` |
| CI 发布（推送 `v*` 标签） | 标签去掉前导 `v`，例如 `v2.1.0` → `2.1.0` |

CI 通过 `-p:Version=<版本>` 注入，**会覆盖 csproj 里的值**。因此打标签前务必让 csproj 的 `<Version>` 与标签一致，否则本地产物与 Release 产物的版本号会对不上。

## 发版步骤

1. 更新 `HourChimeWpf/HourChimeWpf.csproj` 的 `<Version>`，并把 `CHANGELOG.md` 中 `[Unreleased]` 的内容归并到新版本段落、写上日期。

2. 本地验证：

   ```powershell
   dotnet build HourChime.sln -c Release
   powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Version X.Y.Z
   ```

   确认 `artifacts/` 下生成两个 zip 与 `SHA256SUMS.txt`；分别解压运行一次自包含版与框架依赖版（后者需要已安装 .NET 10 桌面运行时），并检查 `%APPDATA%\HourChime\crash.log` 无新增异常。

3. 提交并推送：

   ```bash
   git commit -am "chore: release vX.Y.Z"
   git push origin main
   git tag vX.Y.Z
   git push origin vX.Y.Z
   ```

4. CI 自动执行：Windows runner 构建两个包 → 上传为构建产物（保留 30 天）→ 创建 GitHub Release，附上 `*.zip` 与 `SHA256SUMS.txt`，Release Notes 由 GitHub 根据提交自动生成。

5. 到 Releases 页面核对资产齐全、版本号正确；如需中文说明可手工编辑 Release 描述。

## 手动触发（不发版）

在 Actions 页面选择 **Build** → *Run workflow*，可填写版本号（留空则用 `0.0.0-ci.<run_number>`）。产物仅上传为构建产物，**不会**创建 Release。

## 校验下载包

构建脚本为每个 zip 生成同名 `.sha256` 文件，并汇总成 `SHA256SUMS.txt`。CI 在上传构建产物前会重新计算哈希并核对两类文件，不一致即让工作流失败；Release 会一并附上 `.sha256` 文件。

Windows PowerShell：

```powershell
Get-FileHash .\HourChime-X.Y.Z-win-x64.zip -Algorithm SHA256
```

Git Bash / Linux（zip 与校验文件需在同一目录）：

```bash
sha256sum -c HourChime-X.Y.Z-win-x64.zip.sha256
sha256sum -c SHA256SUMS.txt
```

构建脚本生成的每个 zip 都内含 `HourChime.exe` 与 `LICENSE`（MIT 分发要求）。

## 发布前检查清单

- [ ] csproj `<Version>`、`CHANGELOG.md`、git 标签三者一致
- [ ] 主窗口、托盘菜单、Toast 截图无异常（可用 `/snapshot:`、`/traypreview:` 生成）
- [ ] 两个包都能正常启动，无新增崩溃日志
- [ ] 校验文件齐全（2 个 `.sha256` + `SHA256SUMS.txt`），CI 的 Verify checksums 步骤通过
- [ ] README 中的体积、下载说明仍然准确
- [ ] 文档与徽章中的仓库链接均指向 `OPEN96/HourChime`
- [ ] `LICENSE` 的版权所有者与实际维护者一致
