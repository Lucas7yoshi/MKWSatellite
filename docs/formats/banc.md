# Banc placement scenes (`Banc/*.bcett.byml.zs`)

zstd-compressed BYML. Parsed by `Course/BancScene.cs`. Findings from 1.0 and 1.8 unless noted.

## Scene kinds (by file name prefix)
`Race_<Course>_<Variant>_00[Game03]` (race layout, 47 in 1.0 / 57 in 1.8), `InterStageEx1/2_<A>To<B>_A_00` (open-world routes between courses), `Mission_*`, `BalloonBattle_*`, `CoinBattle_*`, `Parade_*`, `LongRally_*`, `Island_*`, `World_*`, `<Course>_00_Fix` (scenery, e.g. `Rainbow_00_Fix`).

## Root dictionary
| Key | Meaning |
|---|---|
| `FilePath` | Source path, e.g. `Course/Race_Cinema_A_00.bcett.json` |
| `AABB` | `{Min, Max}` bounds |
| `Actors` | Placed objects |
| `Rails` | Graph rails (paths) |
| `CountActor`, `CountRail`, `Collects` | Summary counts (e.g. coins) |

## Actor
| Key | Meaning |
|---|---|
| `Gyaml` | Type, e.g. `Work/Gyml/ActorGParam/CmnObj_ItemBox000_A_00.gear__ActorGParam.gyml` (short name = file name before the first `.`) |
| `Name` | Display name |
| `Hash`, `SRTHash`, `InstanceId` | Identity (u64 hash, transform hash, GUID string) |
| `Translate`, `Rotate`, `Scale` | Transform. `Rotate` is Euler radians, R = Rz·Ry·Rx (rotate about X first): verified for rail points in RailGraph_ParseRail (1.8 `0x710029AD70`), assumed the same for actors. Scale omitted = 1. |
| `Dynamic` | Per-type parameters, e.g. `StartPositionId` |
| `ParentPrefab`, `PrimitiveType`, `IsGridify` | Prefab source, editor primitive (e.g. `Cone`), grid flag |

## Rail
`Name` is the rail kind; `IsClosed`; `Points` is a list of nodes. Kinds seen: `LapPathGraph`, `RoutePathGraph` (AI routes), `GuideGraphRail`, `GravityPath`, `GravityCameraPath`, `GlidePath`, `WaterFlowPath`.

Rail point: `Hash`, `Parent` (rail hash), `Translation`, `Rotate`, `RectOffset` [2], `RectSize` [2], `Dynamic`, optional `Links` (`{Src, Dst, Name: "BasicSignal"}`: graph edges to other points by `Hash`, often to the first point of another rail). Guide rail points also have `Control0`/`Control1` (bezier handles).

Every rail point (all rail kinds) is parsed by RailGraph_ParseRail (1.8 `0x710029AD70`): `Translation`, `Scale`,
`Rotate`, `Control0`/`Control1` (default to the point itself), `RectSize` and `RectOffset` (each `[x, y]`), and the
rail's `IsClosed`.

### Lap path
- `RectSize`/`RectOffset` form the point's gate rectangle in its local X/Y plane (verified: read by the rail parser).
  The gate faces local +Z, which follows the path: median 8–14° between the gate normal and the direction to the next
  point on Boo Cinema, Rainbow Road and Snow Mountain. Centring the rectangle on the offset is inferred, not traced.
- `Dynamic` keys seen across the 57 race scenes: `CheckPoint` (float, 585 nodes), `IsStart` (57, one per scene),
  `IsGoal` (6), `IsLapCheck` (15), `ChangeId` (15), `IsJugemLeft` (24), `LerpType` (145). Names only: their
  gameplay meaning (e.g. what counts as passing a checkpoint) has **not** been traced yet.
- `CheckPoint` numbering restarts per section on multi-section courses like Rainbow Road.

### Route path
`Dynamic` has `AssistLerpType`, `ItemType`, `RecoverType` (meanings unknown).

### Guide rail
`Dynamic.VelocityScale`, `Param1`.

## Facing of placed objects
- `StartPosition`: local +Z = driving direction (within 1° of the lap path).
- Dash panels / glide ramps: local +Z matches the lap direction on Snow Mountain (0–34°) but is **reversed** for Boo
  Cinema's world dash panels (155–173°). User's hypothesis: world dash panels flip to the travel direction (maybe
  only in free roam). Not traced; the viewer shows the authored facing.
- Boost rings (`AccelRing`): 163–174° from the lap direction everywhere, so the model's forward is probably -Z, or
  facing doesn't matter. Drawn without an arrow.

## Areas
Box areas (`PrimitiveType` Cube) are drawn as a unit cube scaled by `Scale`. **The base cube size is assumed, not
verified**, so area extents may be off by a constant factor.

## Notable actor types (Rainbow Road / Boo Cinema)
`CmnObj_ItemBox000_A/B_00`, `CmnObj_Coin000_A_00`, `StartPosition` (`Dynamic.StartPositionId`), `CmnObj_DashBoard000_*`, `CmnObj_GlideBoard000_*`, `CmnObj_AccelRing000_A_00`, `SystemObj_RescueArea_00` (respawn), `SystemObj_ValleyArea_00`, `SystemObj_GlideAreaKiller_00`, `Area_DeleteLinkedActor_00`, `SystemObj_HighSpeedArea_00`, course-specific objects (`CmnObj_TeresaCinema000_A_00` Boos, `SystemObj_RainbowTrain*`).
Stray objects can sit far outside the course (a coin at ~(3420, 12313) on Rainbow Road), so frame views on the paths rather than on all actors.
