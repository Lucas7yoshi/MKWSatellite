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
Areas are drawn as a unit cube (or a unit-diameter circle for `PrimitiveType` Sphere/Cylinder) scaled by `Scale`.
The 1-unit base is **inferred, not traced**: Rainbow's delete areas have scales of 500–2,100 that only make sense as
sizes in units, and rescue areas (10×19) fit the track width. Area types and their developer labels are listed
in scene-layers.md and `CourseObjectClassifier.SystemTypes`.

## Notable actor types (Rainbow Road / Boo Cinema)
`CmnObj_ItemBox000_A/B_00`, `CmnObj_Coin000_A_00`, `StartPosition` (`Dynamic.StartPositionId`), `CmnObj_DashBoard000_*`, `CmnObj_GlideBoard000_*`, `CmnObj_AccelRing000_A_00`, `SystemObj_RescueArea_00` (respawn), `SystemObj_ValleyArea_00`, `SystemObj_GlideAreaKiller_00`, `Area_DeleteLinkedActor_00`, `SystemObj_HighSpeedArea_00`, course-specific objects (`CmnObj_TeresaCinema000_A_00` Boos, `SystemObj_RainbowTrain*`).
Stray objects can sit far outside the course (a coin at ~(3420, 12313) on Rainbow Road), so frame views on the paths rather than on all actors.

## AI routes (`RoutePathGraph`)
Many short route rails branch off the main line. Flags sit in the first point's `Dynamic`; the viewer colours by them,
meanings taken from the key names only (not traced in code): `RoutePriority` (-2..10; negative on alternative lines
such as DK Pass's shortcut between checkpoints 4 and 5), `IsOnlyKillerInItem` (Bullet Bill only), `IsIgnoreItem`,
`IsIgnoreEnemy`, `IsAssistAvailable`, `FastType`, `TrickType`, `ItemGroup`, `RecoverType`.

### Bullet Bill ("Killer") point flags (traced in 1.8)
Every route point's `Dynamic` is read by one parser (1.8: `0x7100AD4600`, keys looked up by MurmurHash3). The booleans
become a bit field on the point: `IsKillerReleasable` 0x800, `IsForbidKillerFinish` 0x1000, `IsOnlyKillerInItem`
0x10000, `IsReversePenaltyInPathOnKiller` 0x40000, `IsKillerNarrowing` 0x80000; `KillerWallTurn` is an int.

The route follower (1.8: `0x71006C24D0`) copies the releasable and forbid-finish bits of each point when it reaches it
and keeps them until the next point. When a Bullet Bill would end:
- after about 840 frames (14 s) it ends anyway, before the flags are looked at;
- if the last point passed has `IsForbidKillerFinish`, it keeps going, so **a flagged point protects the stretch up to
  the next point**;
- if it has `IsKillerReleasable`, it only ends in one particular follower state;
- otherwise it also refuses while a turn value on the follower is above 0.45 (not traced further; likely the
  current segment's turn);
- then it walks the upcoming route points (a queue filled as points are reached, 1.8: `0x7100BEFA80`). The distance
  to the first is measured along its segment direction (skipped if already passed), then segment lengths are added.
  It keeps going if a point **within 30 units** has turn > **0.45** or `ItemType` 1 or 2, or a point **30-50 units**
  ahead has turn > **2.0**. Past 50 units it ends.

A point's **turn** = heading change to the next point / distance to it, in degrees per unit: the angle between the two
points' forward axes (local +Z from `Rotate`) around the first point's up axis (local +Y), `atan2` times 57.2958.
The code reads two axis vectors stored on the runtime point (+0x44, +0x50); that they are local +Y/+Z of `Rotate` is
assumed (it matches the segment headings on Airship Fortress), not traced.
Implemented in `BulletBillRules` (follows one rail, not links into other rails).

So every bend tighter than 0.45°/unit, and its 30-unit lead-in, is protected without any flag. On Airship Fortress
corners run 2-3°/unit and the descending spiral about 2°/unit, so the whole spiral is covered; the ramp up to it is
covered by `ItemType` 2 points (route rail 1 nodes 65-68). `IsForbidKillerFinish` adds explicit stretches on top,
e.g. over the gap before the finish line.
Interstage and Rally routes carry far more flags (e.g. `LongRally_BigTreeToAirshipFortress`: 677).

## Area shapes
An area's actor gyml has `BancDummyShape` (`Cube_c` 33 types, `Cube_t` 14, `Cube` 9, `Sphere` 4, `Cylinder` 3,
`Cylinder_c` 2). Rescue/valley/high-speed/dash areas are `Cube_t`; across all race scenes the lap and route points inside
rescue areas sit 0.3-0.7 of the box height above the actor's origin, so `Cube_t` boxes extend upwards from their origin
(Y 0..1 × Scale) while `_c` ones are centred. Inferred from data, not traced in code.
