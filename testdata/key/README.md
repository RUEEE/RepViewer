# 按键录像测试夹具

这些录像用于核对各作 replay 的原始按键 mask 与 `ReplayKey` 映射。

通常在开场后依次单独按下：`Ctrl`、`Shift`、`Z`、`X`、`C`、`↑`、`↓`、`←`、`→`。TH18 和 TH20 的录制顺序为 `Ctrl`、`Shift`、`Z`、`X`、`C`、`D`、`↑`、`↓`、`←`、`→`；其中 TH18 之后 Ctrl 无效。部分作品不支持 C，部分作品按 C 会同时记录 Ctrl。TH09 只核对 P1。

The fixtures verify per-game raw key masks and their normalized `ReplayKey` values. Keys are pressed individually in the fixed order described above; unsupported or coupled keys intentionally preserve each game's observed behavior.
