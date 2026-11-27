# Replay error reporting requirements

- 对任何作品新增 bug replay、损坏字段或长度恢复逻辑时，都必须通过 `ReplayDocument.Issues` 记录检测到的异常，保留原始字段值。
- Shell 错误缩略图必须使用统一规则：右上角叠加 `assets/thumbnail/Error.png`，机签使用红色斜体；不能只为 TH10 特判。未来其它游戏出现类似 bug replay 时必须沿用此规则。
- 解析警告或错误（Warning/Error）触发错误缩略图；完全无法解析时也应在通用录像图上叠加错误标记。无异常录像保持普通样式。
- 修复副本重新解析后若已无异常，应恢复普通缩略图；不要把错误状态写入原文件或持久化到正常副本。
- 修复功能必须另存，保留输入、随机数、其它未修改字段及 USER/Mod 尾部数据，并在保存前验证重新解码及读回结果。
