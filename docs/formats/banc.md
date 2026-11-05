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
| `Translate`, `Rotate`, `Scale` | Transform. Rotate looks like radians (**unverified**). Scale omitted = 1. |
| `Dynamic` | Per-type parameters, e.g. `StartPositionId` |
| `ParentPrefab`, `PrimitiveType`, `IsGridify` | Prefab source, editor primitive (e.g. `Cone`), grid flag |

## Rail
`Name` is the rail kind; `IsClosed`; `Points` is a list of nodes. Kinds seen: `LapPathGraph`, `RoutePathGraph` (AI routes), `GuideGraphRail`, `GravityPath`, `GravityCameraPath`, `GlidePath`, `WaterFlowPath`.

Rail point: `Hash`, `Parent` (rail hash), `Translation`, `Rotate`, `RectOffset` [2], `RectSize` [2], `Dynamic`, optional `Links` (`{Src, Dst, Name: "BasicSignal"}`: graph edges to other points by `Hash`, often to the first point of another rail). Guide rail points also have `Control0`/`Control1` (bezier handles).

### Lap path
- `Dynamic.CheckPoint` (float) numbers checkpoints; numbering restarts per section on multi-section courses like Rainbow Road (**interpretation unverified**).
- `RectOffset`/`RectSize` are most likely the checkpoint gate rectangle (width/height around the point, oriented by `Rotate`). **Unverified: check in game code before relying on it.**

### Route path
`Dynamic` has `AssistLerpType`, `ItemType`, `RecoverType` (meanings unknown).

### Guide rail
`Dynamic.VelocityScale`, `Param1`.

## Notable actor types (Rainbow Road / Boo Cinema)
`CmnObj_ItemBox000_A/B_00`, `CmnObj_Coin000_A_00`, `StartPosition` (`Dynamic.StartPositionId`), `CmnObj_DashBoard000_*`, `CmnObj_GlideBoard000_*`, `CmnObj_AccelRing000_A_00`, `SystemObj_RescueArea_00` (respawn), `SystemObj_ValleyArea_00`, `SystemObj_GlideAreaKiller_00`, `Area_DeleteLinkedActor_00`, `SystemObj_HighSpeedArea_00`, course-specific objects (`CmnObj_TeresaCinema000_A_00` Boos, `SystemObj_RainbowTrain*`).
Stray objects can sit far outside the course (a coin at ~(3420, 12313) on Rainbow Road), so frame views on the paths rather than on all actors.
