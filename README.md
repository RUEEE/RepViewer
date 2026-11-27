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

RepViewer 是一个面向东方 Project 录像文件（`.rpy`）的 Windows 查看器。当前版本为 **1.4.0.0**。

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

### 1.4.0.0（2026-11-27）

- 支持读取 TH10 重复保存 bug 产生的错误帧数录像，保留原始值并从 Stage 数据长度恢复实际帧数。
- 新增“另存修复版”按钮，修正 TH10 各面帧数，使用 LZSS 字典匹配压缩并重新加密；保留按键、帧率、其它字段及 USER/注释等尾部数据，保存前执行解码和读回验证。
- 错误录像缩略图在右上角叠加 `Error.png`，机签显示为红色斜体；修复后不再检测到异常的副本恢复正常样式。
- 这一错误缩略图规则适用于所有作品，后续新增类似 bug 录像支持时也必须沿用。

#### TH10 重复保存 bug 与修复方法

同一局游戏重复保存录像时，TH10 未在保存前清零 Stage 头中的帧数累加器，导致第二次保存写入实际帧数的两倍、第三次为三倍，以此类推。相邻的数据长度字段和实际输入数据没有同步倍增，游戏播放时可能越过本面的真实输入区，误读帧率或后续 Stage 数据而失去同步。

RepViewer 根据完整 Stage 数据长度 `L = 6 × N + ceil(N / 30)` 恢复记录数 `N`，其中每帧输入占 6 字节，每 30 帧对应一个帧率样本。仅在长度精确匹配时恢复；界面保留错误的原始帧数，同时使用恢复值读取数据并记录异常。

打开可修复录像后，点击“另存修复版”并选择新文件名。修复仅替换解码后各面头部的帧数字段，随后重新进行 LZSS 压缩及 TH10 加密，更新压缩长度和 USER 偏移，保持尾部数据不变。输出会重新解码、比对所有解码字节及逐面按键/帧率后才允许保存。原文件不会被覆盖；游戏内实际播放效果仍需核验。

样本 `th10_19.rpy` 六面的声明帧数均为实际记录数的两倍；恢复后记录数依次为 `8169、11169、13595、15619、15422、19553`。LZSS 修复副本约 71 KB，与原文件约 71 KB 接近。

### 1.3.1.0（2026-11-03）

- TH08 符卡练习的显示编号为 206～222（原始值 205～221）时，难度显示为 `Last Word`，并保留原始难度值。
- TH08 Last Word 录像缩略图使用 `Diff_Lw.png` 难度图。
- TH08 的 Stage 标签按内部 ID 1～9 映射为 `1、2、3、4A、4B、5、6A、6B、7`，内部 Stage ID 保持不变。

### 1.2.1.0（2026-10-18）

- 修复 TH11 录像 `th11_udaa.rpy` 因结束标记与帧率采样数量的边界差异而无法读取的问题。
- 对轻微的 Stage 数据长度不一致尝试恢复完整可用帧，并将帧率读取限制在当前 Stage 内；恢复时记录长度异常。
- 关联 `.rpy` 时自动结束加载了 RepViewer Shell DLL 的缩略图宿主，使新组件能够重新加载。
- 修复 x86 主程序无法检测 x64 缩略图宿主、导致重新关联后仍使用旧解析器的问题。

### 1.2.0.0（2026-09-18）

- 新增红魔乡（原典版，TH06C/1.03）录像支持，包括总体与 Stage 字段、按键列表及各类按键统计图。
- 新增红魔乡（新典版，TH06NC）普通游戏和符卡练习录像支持，并识别游戏模式与符卡编号。
- 使用逐键样本核对 TH06C/TH06NC 的 Ctrl、Shift、Z、X 与方向键映射。
- 为 Shell 图标与缩略图加入 `06c`、`06nc` 标识；新典版使用十一段红白相间的顶部竖条纹。
- 更新 Shell Provider 标识，避免 Explorer 沿用不支持新格式的旧 Provider 和灰色缩略图缓存。

### 1.1.0.1（2026-08-27）

- 修复 TH14.3 与 TH16.5 缩略图错误地固定显示 `1-1` 的问题；缩略图现在使用经过 `+1` 语义转换的 `Day-Scene`。
- 修复 TH14.3 与 TH16.5 的 Day、Scene 显示值，使二者均为原始值 `+1`。
- 为 TH16.5 加入完整的 22 日枚举，并使用 `Sunday(1)`、`Tuesday(3)`、`Nightmare Diary(22)` 格式显示。
- 更新 Shell Provider 标识，使 Explorer 重新关联后不再复用旧版缩略图 Provider 与缓存。

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

RepViewer is a Windows viewer for Touhou Project replay files (`.rpy`). The current version is **1.4.0.0**.

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

### 1.4.0.0 (2026-11-27)

- Added recovery for incorrect TH10 frame counts caused by repeated replay saves, retaining raw values and deriving record counts from stage data lengths.
- Added “Save repaired copy” for affected TH10 replays, using dictionary-based LZSS compression and re-encryption, preserving inputs, FPS, other fields, and trailing USER/comment data, with decode and read-back verification before saving.
- Erroneous replay thumbnails show `Error.png` in the upper-right corner and render the player name in red italics. Repaired copies with no detected issues use the normal style.
- This error-thumbnail convention applies to every game and must also be used for future replay-bug support.

#### TH10 repeated-save bug and repair

When the same run is saved repeatedly, TH10 does not reset the stage frame-count accumulator before saving. The second save records twice the actual count, the third three times, and so on. Stage data lengths and actual inputs do not grow accordingly. Playback can run beyond the real input region, interpret FPS or subsequent-stage data as inputs, and desynchronize.

RepViewer recovers record count `N` from a complete stage length `L = 6 × N + ceil(N / 30)`: six bytes per input record and one FPS sample per 30 frames. Recovery requires an exact length match. The UI retains the incorrect raw count, uses the recovered count for parsing, and records an issue.

Open an eligible replay, click “Save repaired copy”, and choose a new filename. Repair changes only the decoded per-stage frame counts, recompresses with LZSS, re-encrypts using TH10's algorithm, and updates the packed length and USER offset while preserving trailing data. Output is decoded again and checked against all intended decoded bytes and per-stage input/FPS data before saving. The original cannot be overwritten; actual in-game playback still needs verification.

All six declared counts in `th10_19.rpy` are doubled; recovered counts are `8169, 11169, 13595, 15619, 15422, 19553`. The compressed repair is about 71 KB, close to the original file size.

### 1.3.1.0 (2026-11-03)

- TH08 Spell Practice replays with display spell numbers 206–222 (stored values 205–221) now show `Last Word` as the difficulty while preserving the raw difficulty value.
- TH08 Last Word thumbnails now use the `Diff_Lw.png` difficulty artwork.
- TH08 stage tabs map internal IDs 1–9 to `1, 2, 3, 4A, 4B, 5, 6A, 6B, 7`, preserving internal stage IDs.

### 1.2.1.0 (2026-10-18)

- Fixed TH11 replay `th11_udaa.rpy` failing to load because of a boundary difference between the terminal key record and FPS sample count.
- Recover complete available frames from minor stage-length inconsistencies, restrict FPS reads to the current stage, and record length warnings when recovery is needed.
- Automatically stop thumbnail hosts that have loaded RepViewer Shell DLLs when associating `.rpy` files, allowing updated components to load.
- Fixed the x86 application failing to detect x64 thumbnail hosts and leaving an old parser loaded after reassociation.

### 1.2.0.0 (2026-09-18)

- Added replay support for EoSD Classic Edition (TH06C/1.03), including general and stage fields, key lists, and key-statistics charts.
- Added normal-play and Spell Practice replay support for EoSD New Edition (TH06NC), including game-mode and spell-number detection.
- Verified TH06C/TH06NC Ctrl, Shift, Z, X, and direction masks against sequential key-test replays.
- Added `06c` and `06nc` Shell badges; New Edition thumbnails use an eleven-band red/white vertical-striped header.
- Updated Shell Provider identities to prevent Explorer from retaining an older unsupported provider and cached gray thumbnails.

### 1.1.0.1 (2026-08-27)

- Fixed TH14.3 and TH16.5 thumbnails being stuck at `1-1`; thumbnails now use the semantically converted `Day-Scene` values.
- Fixed TH14.3 and TH16.5 Day and Scene display values so both are stored value plus one.
- Added the complete 22-day TH16.5 enumeration using labels such as `Sunday(1)`, `Tuesday(3)`, and `Nightmare Diary(22)`.
- Updated Shell Provider identities so Explorer no longer reuses the obsolete thumbnail provider and cache after reassociation.

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
