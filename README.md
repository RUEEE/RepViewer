# 注意

本项目主要为 vibe coding 产物，不保证完全没问题。

感谢以下项目：
- threp: https://github.com/wasupandceacar/threp
- thhyl: https://github.com/wz520/thhyl
- thhylR: https://github.com/kseptuple/thhylR
- threplay-node: https://github.com/32th-System/threplay-node 
- thrpy-struct: https://github.com/hoangcaominh/thrpy-struct

部分素材取自人形演舞

# Note

This project is mainly vibe coded and is not guaranteed to be completely flawless.

Thanks to the following projects:
- threp: https://github.com/wasupandceacar/threp
- thhyl: https://github.com/wz520/thhyl
- thhylR: https://github.com/kseptuple/thhylR
- threplay-node: https://github.com/32th-System/threplay-node 
- thrpy-struct: https://github.com/hoangcaominh/thrpy-struct

# RepViewer

RepViewer 是一个面向东方 Project 录像文件（`.rpy`）的 Windows 查看器。当前版本为 **1.1.0.0**。

## 功能

- 读取多作正式版、体验版及部分 Mod 录像格式。
- 分离原始值、语义值与由 YAML 控制的本地化显示值。
- 展示总体信息、各 Stage 字段、未知量、隐藏量和 Mod/扩展数据。
- 展示按键、帧率、APS/DPS/DFPS、方向转移统计与可交互折线图。
- 对多 Stage 数字字段绘制折线，并追加同作品、同面数录像进行对比。
- 编辑录像注释及其 Shift-JIS、UTF-8、ANSI 编码。
- 通过 `RepViewer.Shell` 提供 `.rpy` 图标、缩略图和双击打开关联。
- 支持中文/英文 YAML 展示配置、100%～200% 界面缩放以及插件开关。

## 更新记录

### 1.1.0.0（2026-08-20）

- 新增黄昏酒场 ～ Uwabami Breakers ～（`alcostg`）录像支持，包括总体信息、三个 Stage、按键、帧率和中英文展示配置。
- 为黄昏酒场 Shell 缩略图加入 `alco` 标识、固定的 `Isami.png` 角色图和独立配色。
- 分离 C 与 Ctrl 的内部按键语义，统一显示为 `Z`、`X`、`C`、`D`、`Δ`（Shift）和 `Σ`（Ctrl）。
- 使用逐作按键录像核对 TH06～TH20、文花帖系和黄昏酒场的原始 mask；支持同一帧同时显示 C 与 Ctrl，并为未识别位追加原始十六进制值。
- 修复 TH12.5 三字节按键流、TH16.5 Stage 头偏移、TH20 按键状态累积，以及结束标记被识别成全按键的问题。
- 修复黄昏酒场关卡数偏移错误导致多 Stage 录像只显示一个 Stage 的问题。
- 更新 Shell Provider 标识与部署检测，避免 Explorer 继续加载旧解析器；修复“否且不再提示”仍刷新 Explorer，以及未改变关联设置却触发刷新的问题。
- 修复多个折线图之间悬停导致视图跳动的问题，并为所有折线图加入按当前区间或全图导出 CSV 的菜单。
- 更新中英文界面文本，并继续保留发布后可直接修改的 YAML 展示配置。
- 将逐作按键核对录像收录到 `testdata/key`，作为解析回归夹具。

## 项目结构

- `RepViewer.Core`：二进制格式、原始值/语义值、按键与帧率、USER 注释和 Mod 数据。
- `RepViewer.Presentation`：YAML 本地化配置及语义值到显示值的格式化。
- `RepViewer.Plugins`：与界面渲染无关的字段、帧率、按键频率和方向统计视图。
- `RepViewer.App`：WPF 主程序。
- `RepViewer.Shell`：独立的 Explorer 缩略图与分作图标 COM Provider。
- `testdata/key`：按固定顺序录制的逐作按键录像，用于核对原始 mask 和统一按键语义。

## 构建

需要 .NET 8 SDK。构建产物统一输出到 `Debug` 或 `Release`。

```powershell
dotnet build .\RepViewer.slnx -c Debug --configfile .\NuGet.Config
dotnet build .\RepViewer.slnx -c Release --configfile .\NuGet.Config
dotnet build .\src\RepViewer.App\RepViewer.App.x64.csproj -c Release --configfile .\NuGet.Config
dotnet msbuild .\RepViewer.Portable.proj -t:Publish
```

`Release\portable` 是依赖 .NET 8 Desktop Runtime 的便携目录，同时包含 x86/x64 主程序、Shell Provider、图标和可在发布后继续修改的 `presentation` YAML。文件关联会修改当前用户的 Explorer 注册信息，因此不会在构建过程中自动执行。

用户设置保存在 `%LocalAppData%\RepViewer\settings.json`。首次运行且尚无配置时，Windows UI 语言属于中文语言族则使用 `zh-CN`，否则使用 `en-US`。

- [架构说明](docs/architecture.md)
- [字段冲突记录](docs/field-conflicts.md)

---

# RepViewer (English)

RepViewer is a Windows viewer for Touhou Project replay files (`.rpy`). The current version is **1.1.0.0**.

## Features

- Reads replay formats from multiple full games, trials, and selected mods.
- Keeps raw values, semantic values, and YAML-driven localized display values separate.
- Shows general data, per-stage fields, unknown/hidden values, and mod/extension data.
- Visualizes keys, frame rate, APS/DPS/DFPS, direction transitions, and interactive line charts.
- Plots numeric fields across stage sequence and compares replays from the same game with the same stage count.
- Edits replay comments using Shift-JIS, UTF-8, or ANSI encoding.
- Provides `.rpy` icons, thumbnails, and double-click association through `RepViewer.Shell`.
- Supports Chinese/English YAML presentation, 100%–200% interface scaling, and configurable plugins.

## Changelog

### 1.1.0.0 (2026-08-20)

- Added replay support for Uwabami Breakers (`alcostg`), including general data, all three stages, keys, FPS, and Chinese/English presentation files.
- Added the `alco` Shell thumbnail badge, fixed `Isami.png` character artwork, and a dedicated color scheme.
- Separated C and Ctrl in the internal key model and standardized display labels as `Z`, `X`, `C`, `D`, `Δ` (Shift), and `Σ` (Ctrl).
- Verified raw key masks from TH06 through TH20, the photography games, and Uwabami Breakers; simultaneous C/Ctrl is retained and unrecognized bits append the original hexadecimal value.
- Fixed TH12.5 three-byte key streams, the TH16.5 stage-header offset, accumulating TH20 key states, and replay terminators being shown as every key pressed.
- Fixed the Uwabami Breakers stage-count offset that made multi-stage replays appear to contain only one stage.
- Updated Shell Provider identities and deployment detection to prevent Explorer from retaining an obsolete parser; fixed unnecessary Explorer refreshes, including the “No and never ask again” path.
- Fixed chart jumping when hovering between key-rate plots and added CSV export for either the selected range or the complete series to every line chart.
- Updated Chinese and English UI text while retaining editable YAML presentation files in published builds.
- Added per-game key verification replays under `testdata/key` as parser regression fixtures.

## Projects

- `RepViewer.Core`: binary formats, raw/semantic values, keys and FPS, USER comments, and mod data.
- `RepViewer.Presentation`: YAML localization and semantic-to-display formatting.
- `RepViewer.Plugins`: renderer-neutral property, FPS, key-rate, and direction-statistics views.
- `RepViewer.App`: the WPF desktop application.
- `RepViewer.Shell`: isolated Explorer thumbnail and per-game icon COM providers.
- `testdata/key`: per-game key replays recorded in a fixed order for raw-mask and normalized-key regression checks.

## Build

The .NET 8 SDK is required. All build products are centralized under `Debug` or `Release`.

```powershell
dotnet build .\RepViewer.slnx -c Debug --configfile .\NuGet.Config
dotnet build .\RepViewer.slnx -c Release --configfile .\NuGet.Config
dotnet build .\src\RepViewer.App\RepViewer.App.x64.csproj -c Release --configfile .\NuGet.Config
dotnet msbuild .\RepViewer.Portable.proj -t:Publish
```

`Release\portable` is a framework-dependent deployment for the .NET 8 Desktop Runtime. It contains the x86/x64 applications, Shell providers, icons, and mutable `presentation` YAML. File association changes the current user's Explorer registration and is therefore not performed during the build.

User settings are stored in `%LocalAppData%\RepViewer\settings.json`. On first run without a saved configuration, a Chinese Windows UI language selects `zh-CN`; all other languages select `en-US`.

- [Architecture](docs/architecture.md)
- [Field conflicts](docs/field-conflicts.md)
