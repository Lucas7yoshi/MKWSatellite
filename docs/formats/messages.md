# Messages (in-game names)

Checked in the 1.8 data (`Product.180`).

## Files

`Mals/<Region><lang>/` (e.g. `USen`, `EUen`, `JPja`) holds plain (uncompressed) SARC archives with the `.msarc`
extension: `MsgMain.msarc`, `LayoutMsg.msarc`, and per-update `Ver110.msarc` ... `Ver180.msarc`, plus `Drive.msbp`.
`MsgMain.msarc` contains MSBT files: `Course.msbt`, `CourseArea.msbt`, `BattleCourse.msbt`, `Mission.msbt`,
`Race.msbt`, `Menu.msbt`, `Driver.msbt`, `Help.msbt`, ...

MSBT (`MsgStdBn`): BOM `FF FE` (little-endian), encoding byte at 0x0C (1 = UTF-16), section count at 0x0E, sections
from 0x20, each a 16-byte header (magic, size) and padded to 16. `LBL1` maps labels to text indices (hash groups of
`count, offset`; each entry is a length-prefixed name and an index), `TXT2` holds the strings. Inline tags start with
`0x0E` (group, type, parameter size, parameters) and `0x0F` closes one. Reader: `Formats/Text/Msbt.cs`.

## Scene names

`Formats/Text/CourseNames.cs` maps scene files to English names (all 689 listed scenes get one):

| Scenes | Source |
|---|---|
| `Race_<Course>_<V>_00` | `Course.msbt` label `Name_Race_<Course>_<V>_00`. Only one layout per course has a label (`WestCity_D` = Crown City, `Central_A` = Peach Stadium, `SFCMarioCircuit_G` = Mario Circuit); the viewer shows the others as the course name plus their letter, e.g. "Crown City (B)". What the other layouts are used for is not taken from messages. |
| `InterStageEx1/2_<A>To<B>_*`, `LongRally_<A>To<B>_*` | `CourseArea.msbt` `Name_<Course>` or `Name_<Course>_00` for both ends, shown "A → B". The split at "To" is tried at each occurrence (`...ToToadFactory`). |
| `Balloon/Coin/BombBattle_<Stage>` | `BattleCourse.msbt`, numeric labels `1701`–`1708`, linked by `System/GymlArchive/general_format` `Gyml/BattleCourseSelectUIInfo/{Balloon,Bomb,Coin}.bgyml`: `CourseInfo[]` with `UITextureBaseName` (the stage, e.g. `Volcano_00` = Big Donut, `Ice_01` = DK Pass) and `CourseMessageLabel`. |
| `Mission_<Id>_Fix` | `Mission.msbt` label `Mission_<Id>`: the mission's description ("Gather blue coins on ..."). |

The RSDB `Course` table and `CourseGParam` files carry no message label (only the Japanese developer `DispName`).
