# Field decisions and remaining audits

The previously reported semantic conflicts have been decided as follows and are now part of the Core schema.

| Format | Decision |
|---|---|
| TH07 | `+0x08` is `Cherry`; `+0x10` is `CherryPlus`. |
| TH09 | `+0x07` is `PlayerTypeB`. |
| TH10 | `+0x14` is `Faith`; `+0x18` is `FaithGauge`; life is at `+0x1c`. |
| TH11/12 | `+0x14` uses the stable name `Piv`. TH11 header `+0x0c` is a 32-bit Unix timestamp. |
| TH12.8 | `+0x14` is `FreezePower`; `+0x80` remains `Motivation` (the game's motivation/life system). |
| TH13/14 | Stage score begins at `+0x1c`; subsequent fields follow the thhylR layout. |
| TH14.3 | Header uses day, scene, stage, main/sub item and item-count fields. |
| TH15/16/17 | Point-score fields use the stable ID `Piv`. |
| TH16.5 | Header uses day, scene, stage, power level and retry fields. |
| TH18 | Retail stage layout follows thhylR. Cards and spell-time arrays are parsed as semantic collections. |
| TH20 | Header and stage layouts follow thhylR; point score retains the stable ID `Piv`. |

## Evidence

The repository sample `th11_udLNN.rpy` contains `1711607412` at decoded header `+0x0c`, which is the plausible Unix timestamp `2024-03-28T06:30:12Z`.

The TH18 sample `th18_ud1vwm.rpy` produces per-stage card counts `3, 4, 5, 6, 6, 7` and spell-time counts `2, 3, 3, 3, 4, 6`, confirming the retail offsets used by the new structs.

## Remaining format split

TH17 and TH18 trials now have dedicated magic dispatch, format classes and packed structs; TH18 trial uses its own `0x96c` stage layout, card array and spell-time offset. The sample sets contain no trial replay with which to validate these paths.

The remaining dedicated splits are TH07 trial revisions and the version-selected TH13/TH15/TH16 trials; TH20 trial also still needs its own struct despite currently matching retail in thhylR. These will not reuse a retail struct as an alias.

thhylR only diagnoses the TH07/08/09 localized-executable checksum mismatch; it does not contain a repair writer. Core now reports this condition as `localized-executable-checksum`. A real repair still requires a tested inverse compressor/encrypter and will not be represented as an in-memory decoded-byte patch.

## TH08 sparse pointer table

TH08 stores nine stage slots followed by nine FPS slots. Empty slots may contain zero or otherwise unusable/non-monotonic values. The Core reader mirrors thhylR by validating all 18 pointers, preserving slot identity, and pairing stage slot `n` with FPS slot `n + 9`. This is required for spell-practice files such as `th8_ud00jK.rpy`, which contains only stage slot 9.
