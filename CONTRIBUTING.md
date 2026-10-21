# Contributing to PaPerMySelf

Thanks for helping improve PaPerMySelf. This repository is a downstream development edition of [snownico0722/PaperTodo](https://github.com/snownico0722/PaperTodo); upstream behavior, licensing and attribution still matter here.

## Before you start

- Use Windows x64 with the .NET 10 SDK, PowerShell 7.2+ and Git. Python 3 is needed only for README synchronization.
- Read [AGENTS.md](AGENTS.md), [doc/ARCHITECTURE.md](doc/ARCHITECTURE.md) and the relevant entries in [doc/DECISIONS.md](doc/DECISIONS.md) before changing runtime, persistence, Edge Capsule or plugin behavior.
- Keep user data out of commits: `data.json`, `data.backup.json`, `note-assets.lmdb`, plugin data, credentials, local SDKs and build/package output are ignored by design.

## Build and run

```powershell
dotnet build PaPerMySelf.csproj -c Release
./start-dev.ps1 -Build
```

The development instance uses the separate `PaPerMySelf` single-instance namespace and does not share data with an installed desktop copy.

## Verify

```powershell
./tools/testing/Run-Checks.ps1 -Group all -List
./tools/testing/Run-Checks.ps1 -Group regression -Project ReadingChecks
./tools/testing/Run-Checks.ps1 -Group persistence
```

Checks use isolated data. Real window and input checks need an interactive Windows desktop; a passing targeted run is not a substitute for multi-monitor or mixed-DPI release testing.

For the local package:

```powershell
./tools/Publish-Local.ps1
```

This creates one Release / win-x64 / self-contained / single-file EXE under `artifacts/packages/`. It does not create a GitHub Release.

## Pull requests

- Branch from `main` and keep each pull request focused on one behavior.
- Explain what changed, why, and which data or compatibility boundaries are affected.
- Include the checks you actually ran. If a check could not run, say why instead of claiming a full pass.
- Add or update behavior checks for persistence, synchronization, external writes and other high-risk boundaries.
- Update user-visible text in all four languages (`Resources/`, plus reading strings) when a change is user-visible.
- Update `CHANGELOG.md` and `CHANGELOG.zh.md` under `Unreleased` only for user-visible differences that will ship.
- Do not include unrelated formatting, generated files, local data or private SDK paths.
- Preserve the original license and third-party notices. Do not represent a downstream build as an upstream release.

## Architecture boundaries

- Use `StateStore` for core state. Do not add a parallel authoritative writer.
- Keep `PaperCommandService` as the shared mutation boundary for external Paper/Todo/Note writes.
- Keep one `EdgeCapsulePresenter` authority per paper and keep queue/geometry ownership in the existing coordinator and geometry code.
- Keep plugin business state in `PaperBodyPluginDataStore`; do not move it into `data.json`.
- Keep the built-in Note editor on the shared `MarkdownTextBox` surface.

When a change intentionally alters one of these boundaries, discuss the trade-off first and update the architecture or decision records in the same change.

## Commit style

Use a clear, imperative summary. The repository history mixes Chinese and English messages; either is acceptable, but keep the subject short and the body focused on the reason. Do not mix unrelated changes in one commit.

## Security

Do not report vulnerabilities in a public issue. See [SECURITY.md](SECURITY.md).

## 中文说明

- 构建：`dotnet build PaPerMySelf.csproj -c Release`，启动：`./start-dev.ps1 -Build`。
- 提交前运行受影响的行为检查；真实窗口输入检查需要交互式桌面。
- 不提交 `data.json`、图片数据库、插件数据、WebDAV 凭据、本地 SDK 或打包产物。
- 修改用户可见文本时同步简体中文、英文、日文、韩文资源。
- 保留原项目许可与第三方声明；这是下游开发版，不是上游正式发布。
