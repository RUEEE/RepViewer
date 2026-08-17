# Architecture

The dependency direction is one-way:

`Formats -> Core semantic document -> Presentation YAML -> UI / exporters / plugins / Shell`

## Core milestone

- Every replay family is selected by magic and parsed by a dedicated `ReplayFormat` class.
- Binary layouts are explicit packed structs. Reflection projects their fields into a stable tree.
- Every value carries `RawValue`, `SemanticValue`, source field name and decoded offset.
- `SemanticValue` contains game meaning (for example, a stored score of 123 becomes 1230). It never contains translated enum labels, number grouping, UI punctuation or colors.
- Unknown bytes remain available as offset-addressed `UnknownValue` instances. A field is removed from that list as soon as its meaning and type are known.
- Trial formats are to be separate formats and structs; they are not aliases or overrides of retail formats.
- Raw and normalized key streams, FPS samples and direction-transition statistics are view-neutral Core data.
- Per-stage spell times retain the encoded 32-bit value and expose decoded seconds. TH11 uses its dedicated legacy decoder.
- TH18 cards are semantic records containing card ID, cooldown frames and cooldown seconds; localized card names remain Presentation data.
- The original file bytes are retained for diagnostics and future reversible editing.
- Trailing `USER` blocks, including comment decoding and safe comment replacement with a caller-selected encoding, are supported.
- Every USER/extension block retains its marker, ID and unmodified payload bytes. Mod metadata is raw-first: known `PRAC` payloads are detected without requiring JSON, and JSON is only an optional parsed projection.

`ReplayDocument.UserData.Blocks[*].Data` is the authoritative API for opaque extension/mod payloads, including formats unknown to Core. `ReplayDocument.ModMetadata` is only a convenience detector for the currently recognized `PRAC` marker; its `RawData` remains authoritative and `ParsedJson` may be null.

## Projects

- `RepViewer.Presentation`: locale-specific YAML loading, enum labels and display formatting.
- `RepViewer.Plugins`: plugins read semantic fields and return view-neutral properties, series, tables and heat maps.
- `RepViewer.App`: WPF main window (Open, Save comment/repair, Export, Settings).
- `RepViewer.Shell`: isolated Explorer icon, metadata and thumbnail provider using the summary projection.

Presentation resources are arranged as `presentation/<locale>/main.yaml` and `presentation/<locale>/<game>.yaml`. `main.yaml` contains application text and common fields; game files override stable semantic field IDs. Locale spelling accepts either `zh-CN` or `zh_cn`, with `en-US` as the fallback.

The current YAML reader intentionally supports a safe declarative subset: nested mappings and scalar values. Semantic arithmetic remains in Core. Presentation formats currently include number, decimal, percent, duration, datetime, hex and enum mapping; a general expression evaluator is not enabled.

`RepViewer.Shell` is a separate COM-hosted assembly. It catches parser/rendering failures and returns a generic replay image so malformed files cannot propagate managed exceptions into Explorer.

## UI and plugin boundaries

The desktop UI uses an outer `General / Stage 1 ... Stage N` tab strip. A stage page is created lazily when first selected and contains inner tabs emitted by plugins: stage properties, per-frame keys, FPS samples, FPS chart and direction transitions.

Charts are rendered by the reusable `InteractiveLineChart`. Plugin output supplies numeric series, units and stage boundaries; the control owns axes, adaptive major/minor grids, average lines, stage separators, wheel zoom and drag panning.

Repairs use `IReplayRepairPlugin`, separate from `IReplayViewPlugin`. A repair plugin can diagnose a replay and optionally return replacement bytes, but it cannot write files. The App owns risk confirmation, destination selection, saving and reparsing verification. This prevents third-party repair logic from silently overwriting the source replay. The built-in TH07/08/09 compatibility plugin currently diagnoses only because the inverse compressor/encrypter is not yet verified.

All projects inherit `Directory.Build.props`: Debug and Release outputs go to the repository-level `Debug` and `Release` directories respectively.
