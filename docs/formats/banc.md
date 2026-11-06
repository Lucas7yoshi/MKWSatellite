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
`Name` is the rail kind; `IsClosed`; `Points` is a list of nodes. All kinds and what they do (grind rails, object
paths, glide paths...) are in [rails.md](rails.md). A rail may also carry `Dynamic`, `ParentPrefab` and `Parent`
(the owning actor's hash, e.g. the fence under a `ColRailSlidePath`).

Rail point: `Hash`, `Parent` (rail hash), `Translation`, `Rotate`, `RectOffset` [2], `RectSize` [2], `Dynamic`, optional `Links` (`{Src, Dst, Name: "BasicSignal"}`: graph edges to other points by `Hash`, often to the first point of another rail). Guide rail points also have `Control0`/`Control1` (bezier handles).

Every rail point (all rail kinds) is parsed by RailGraph_ParseRail (1.8 `0x710029AD70`): `Translation`, `Scale`,
`Rotate`, `Control0`/`Control1` (default to the point itself), `RectSize` and `RectOffset` (each `[x, y]`), and the
rail's `IsClosed`.

### Lap path
- `RectSize`/`RectOffset` form the point's gate rectangle in its local X/Y plane (verified: read by the rail parser).
  The gate faces local +Z, which follows the path: median 8–14° between the gate normal and the direction to the next
  point on Boo Cinema, Rainbow Road and Snow Mountain. Centring the rectangle on the offset is inferred, not traced.
- The parser stores them on the generic rail node at +0x50 (size X/Y) and +0x58 (offset X/Y), with a direction from
  `Rotate` at +0x60 (1.8 `RailGraph_ParseRail` 0x710029AD70). The code that tests a racer against the gate has not
  been found; the gameplay lap object (0x5A0 bytes) keeps a gate centre at +0x18 and half-extents at +0x2C/+0x30
  (default 40/40 from its vtable +0x50), read by its bounding-box helper 0x71007E84A0.
- **Gates and lap progress (traced, 1.8).**
  - Graph build `0x7100799270` creates each lap point and calls `0x71007993A0(point, record)`: position (+0xC) =
    `Translation`; rows +0x38/+0x44/+0x50 = right/up/forward from `Rotate` (right = (cy·cz, cy·sz, -sy)); half-width
    +0x2C = **`RectSize.X`**, half-height +0x30 = **`RectSize.Y`** (unhalved; `Scale` is the fallback).
  - `0x71007EE5D0` stores `RectOffset` at +0x24 and sets the gate centre +0x18 = position + `RectOffset.X` · right +
    `RectOffset.Y` · up. So **a gate spans offset ± size** on each axis: RectSize is a half-extent, RectOffset a full
    shift.
  - The lap class's Dynamic parser (`0x7102990F20`) reads optional `Size` / `Height` (MurmurHash3 0xB4A58247 /
    0xC3DF69CD, never present in 1.8 lap paths) and falls back to 40 only when the value is <= 1.
  - Locating a racer (`0x7100AAD950`): for each segment a→b, t = d_a / (d_a - d_b) with d = signed distance to the
    gate plane (through the centre, normal = forward row); t must be in [0, 1]. Then `RailPoint_IsInSegmentVolume`
    (0x71007F34D0, margin 0) checks |dot(right, p - centre(t))| <= lerp(half-width) and dot(up, ...) >= -lerp(height)
    (upper bound optional), with centre and half-extents lerped and the rotation slerped (quaternions). Of the passing
    segments the nearest point wins.
  - So on a bend the zone's sides **bow outward** past the straight lines between the gate corners (at mid-segment
    the half-width along the chord grows by about 1/cos(θ/2) for a turn of θ). The viewer draws this
    (`LapZone`, "Lap progress zones"), slicing each segment into cross-sections: the plane of constant t intersected
    with the side and floor planes. `LapZoneTests` checks the drawn edges against a port of the game test.
  - `Dynamic.LerpType` (MurmurHash3 0x3C0AED52, stored at +0x530 by the lap Dynamic parser `0x7102990F20`) makes a
    side straight: 1 = both sides, 2 = the +right side, 3 = the -right side, 0/absent = neither. A side is straight
    only when **both** gates of the segment ask for it; it is then the plane through the two gates' edges containing
    the interpolated up axis. 1.8 race scenes use it on short runs of 1-10 nodes (values 1-3 only).
  - Being in no zone at all marks the racer's lap position invalid and leads to a Lakitu rescue (out-of-bounds.md,
    "Lap-out").
  - Confirmed in game (user, 2026-11-05): driving around the old half-size, offset/2 gates at SFC Mario Circuit G
    checkpoints 1-3 still counted; with the traced rule those gates span the corridor wall to wall.
  - Earlier guesses (centre at offset / 2, size as full width, `mkwsat ground`/`gatefit`/`gateturn` statistics) are
    superseded; the tools remain for checking placements against geometry.
- Lap nodes are not all on the ground: on Airship Fortress race nodes 38-40 rise from 742 to 778 over the launch onto
  the airship, so those gates hang in the air on the flight path.
- `Dynamic` keys seen across the 57 race scenes: `CheckPoint` (float, 585 nodes), `IsStart` (57, one per scene),
  `IsGoal` (6), `IsLapCheck` (15), `ChangeId` (15), `IsJugemLeft` (24), `LerpType` (145, traced above). Otherwise names only: their
  gameplay meaning (e.g. what counts as passing a checkpoint) has **not** been traced yet.
- `CheckPoint` numbering restarts per section on multi-section courses like Rainbow Road.

### Route path
`Dynamic` has `AssistLerpType`, `ItemType`, `RecoverType` (meanings unknown).

### Guide rail
`Dynamic.VelocityScale`, `Param1`, `Param2`: paths followed by moving objects that link to them (rails.md).

## Facing of placed objects
- `StartPosition`: local +Z = driving direction (within 1° of the lap path).
- Dash panels / glide ramps: local +Z matches the lap direction on Snow Mountain (0–34°) but is **reversed** for Boo
  Cinema's world dash panels (155–173°). User's hypothesis: world dash panels flip to the travel direction (maybe
  only in free roam). Not traced; the viewer shows the authored facing.
- Boost rings (`AccelRing`): 163–174° from the lap direction everywhere, so the model's forward is probably -Z, or
  facing doesn't matter. Drawn without an arrow.

## Areas
An area's volume comes from its **Phive shape**, in local units multiplied by `Scale` (see "Area shapes" below).
Area types and their developer labels are listed in scene-layers.md and `CourseObjectClassifier.SystemTypes`.

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
Areas with collision follow actor gyml `Components.PhysicsRef` (PhysicsGParam, `phwe_general_format` archive) →
`ControllerSetResourcePath` (Phive ControllerSetParam, `phive_general_format`) → `ShapeNamePathAry[0].FilePath` (Phive
ShapeParam) → `Box[0]` with `Center`, `HalfExtents` and optional `OffsetTranslation`. Values in 1.8:

| Shape | Types |
|---|---|
| centre (0,-1,0), half 1: **Y -2..0, hangs below the origin**, X/Z ±1 | every `BancDummyShape: Cube_t` area with physics: Rescue, RescueAreaMovable, Valley, HighSpeed, GlideAreaKiller, NoJA, RideKeepOut, Glide |
| centre 0, half 1 | CreateLinkArea, GigantDKBounceArea, ForbidReverseArea |
| offset (0,1,0), half 1 | EffectAreaBox |
| half 0.5 | BirdArea |

So a box is **2 × Scale** across, not 1 × Scale as the viewer first assumed, and `Cube_t` boxes extend downward.
Check (`mkwsat areafit`): on Rainbow Road all 18 high-speed areas contain the lap path (the old guess: 2), while
rescue, valley and ride keep-out areas contain none of it, as off-road volumes should. Ride keep-out areas ("ライド
進入禁止エリア(壁)", wall-ride keep-out) sit at wall height above the road, e.g. Mario Bros. Circuit's start arch.

Areas without a physics shape (`Area_EnvCube`, `NoEntryArea`, `Area_DeleteLinkedActor`, camera/sound areas...) fall
back to `BancDummyShape`: `*_t` as the `Cube_t` box above, anything else centred with half-extents 1. That fallback is
a guess and the inspector says so.
