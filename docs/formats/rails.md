# Rail kinds (paths)

Every `Rails[]` entry in a Banc scene has a kind (`Name`, also the `GraphRailDefGParam` file in `Gyaml`). The kinds
are listed in `RSDB/RailDefine.Product.180.rstbl.byml.zs` with a `RailType` (`Default` or `RectEdge`) and refs to
`BancGParam` tables (`...PathPoint`, `...Path`) that define their parameters. Those tables are editor-only: they are
not in the romfs, so what a parameter does has to come from code or from the data around it.

The viewer's `RailKinds` (Formats) holds one line per kind; the inspector shows it, the rail's length and node count,
its prefab, the actor it belongs to, and the objects that link to it.

## Overview (1.8)

Counts over all 57 race scenes (race) and the 160 `World_Game03` tiles (world).

| Kind | Race | World | What it is | Basis |
|---|---|---|---|---|
| `LapPathGraph` | 100 | - | Lap progress, checkpoints | traced, see banc.md |
| `RoutePathGraph` | 1963 | - | AI / Bullet Bill / item routes | partly traced, see banc.md |
| `RailSlidePath` | 452 | 2520 | Grind rails | data + code (below) |
| `ColRailSlidePath` | 161 | 4554 | Grind rails on fences and guardrails | data + code (below) |
| `GuideGraphRail` | 79 | 318 | Paths that moving objects follow | data (below) |
| `GlidePath` | 9 | 54* | Glide guidance; `IsCannon`, `Ascend` | names only |
| `PullPath` | - | 80* | `Power`, `IsFitDir` per node | names only |
| `WaterCascadePath` | - | 164* | Waterfalls | names only |
| `WhirlpoolPath` | - | 6* | Whirlpools | names only |

\* only in `WorldResident.bcett.byml.zs`, which courses do not load (scene-layers.md).

`RailDefine` also lists `CitizenPath`, `GravityPath`, `GravityCameraPath`, `OffroadPath`, `ParkingPath`, `RidePath`
and `WaterFlowPath`. None of them appear in the 1.8 race scenes or world tiles.

## Curves (all kinds)

Rails are not polylines: a segment can bend through Bezier handles stored on its nodes. Traced in 1.8; the code is
shared by every rail kind.

- **Parsing.** `RailGraph_ParseRail` (`0x710029AD70`) reads each node's `Translation`, `Control0`, `Control1` and
  `Rotate`. A missing handle becomes the node's own position. `Rotate` becomes the node's up vector (local +Y, see
  `CourseMathTests`).
- **Segments.** `RailBody_AddPoint` (`0x710029A9A0`) chains the nodes into segments: node i to node i+1, plus last to
  first when `IsClosed`. `Links` to other rails are graph edges, not segments, so they stay straight.
- **Shape.** `RailSegment_SetCurveType` (`0x7100980E00`) looks at the two inner handles: the start node's `Control1`
  and the end node's `Control0`. A handle counts when any axis is more than 0.001 from its node.
  - No handles: straight line.
  - One handle: quadratic Bezier through it.
  - Both handles: cubic Bezier (P0, start `Control1`, end `Control0`, P1).
  - So `Control0` bends the segment coming into a node and `Control1` the one going out.
- **Use.**
  - `RailSegment_Evaluate` (`0x710070AC60`) and `RailSegment_TangentAt` (`0x7102A95660`) evaluate these curves.
  - The grind-rail search (`RailSlideSearcher_FindNearest`) inlines the same evaluation and tests 10 steps per
    segment.
  - `RailSegment_UpAt` (`0x710070AE50`) slerps the two nodes' up vectors, so node `Rotate` sets the rail's roll.
  - The game's debug draw (`RailSegment_DebugDraw`, `0x7102A95730`) uses 9–16 steps per curved segment.
- A subclass of the segment (`0x7102A96680`, flag at +0x90) evaluates a Hermite curve from tangents instead. It is not
  driven by any Banc key we know of, and the viewer ignores it.

Usage in 1.8 (race scenes + world tiles; `mkwsat rails`):

| Kind | Rails | Curved | Segments: line / one handle / cubic |
|---|---|---|---|
| `RailSlidePath` | 2972 | 938 | 11087 / 2378 / 6783 |
| `ColRailSlidePath` | 4715 | 1306 | 3409 / 2612 / 0 |
| `GuideGraphRail` | 397 | 322 | 661 / 187 / 1939 |
| `WaterCascadePath` | 164 | 156 | 8 / 30 / 132 |
| `PullPath` | 80 | 1 | 211 / 4 / 7 |
| `LapPathGraph` | 100 | 0 | 2674 / 0 / 0 |
| `RoutePathGraph` | 1963 | 1 | 16979 / 2 / 0 |
| `GlidePath`, `WhirlpoolPath` | 69 | 0 | all straight |

- Each curved `ColRailSlidePath` has a single node with both handles, which bends the segment on either side of it.
- Lap paths and AI routes are effectively straight. The straight-segment logic for lap zones and Bullet Bill
  look-ahead is therefore unaffected.
- `Marking` (on some grind rails and their nodes) is only read by the actor placement parser
  (`BancPlacement_ParseTransformMarkingScreen`), not by the rail parser. It is probably editor data (**unverified**).

The viewer draws every segment along its curve (`RailCurve`, at least 8 samples, about one per unit up to 64). It
measures rail length along the curve, and shows the handles of the selected rail in yellow.

## Grind rails (`RailSlidePath`, `ColRailSlidePath`)

- **Both kinds are grind rails.** `0x71002A1140` adds `CountRail.RailSlidePath` and `CountRail.ColRailSlidePath`
  of a scene into one count.
- **`ColRailSlidePath`** has no parameters. Its `Parent` is the hash of a fence or guardrail actor:
  `CmnObj_Guardrail000_*`, `CmnObj_FenceWood000_A_00`, `CmnObj_FenceSnowMountain000_*` and similar. Its prefab is the
  actor's own Houdini output (`Houdini/Auto_Game03_CmnObj_Guardrail000_A_00_Replace`, ...). So these are the rails
  along the tops of fences, and they belong to that object.
- **`RailSlidePath`** rail-level `Dynamic`:
  - `RailSlideType` (int, MurmurHash3 0xE747C8D1) and `IsAngleLimit` (bool). Both are read when the rail is set up
    (`0x71005F7FB0`, which stores them at +0xE8 / +0xEC).
  - What the game does with `RailSlideType` is not traced. By the prefabs that use each value:

| `RailSlideType` | Rails (world + race) | From | IsAngleLimit |
|---|---|---|---|
| 0 (absent) | 2559 | hand-placed in course and ground prefabs; generated fixed parts and decoration | 76 of them |
| 1 | 231 | `Houdini/Auto_Game03_TrackParts000_A_00_RailslidePath`: edges of the generated track pieces | always |
| 3 | 134 | `Houdini/Auto_Game03_Wire_RailslidePath`: wires and power lines | never |
| 4 | 30 | `Houdini/Auto_Game03_Wire_RailslidePath`: a second kind of wire | never |
| 5 | 18 | `Resort_Gnd_A_00`, `LagoonCity_Gnd_A_00` | always |

- **Per-node `Dynamic`:** `IsAngleLimit` (334 nodes) and `IsHighPriority` (71 nodes, mostly in the race-scene
  `Audience_Race_*` prefabs).
  - The rail search (`0x710070B360`, slot 11 of the searcher vtable `0x710367F380`) finds the nearest point on the
    rail's segments. Segments are sampled as linear, bezier or cubic, according to the node's `Control0`/`Control1`.
  - It returns that node's `IsAngleLimit` (or the rail's default, if the node does not set one) and its
    `IsHighPriority`, together with the position, tangent and up vector.
  - What the kart does with these two flags is not traced. The names suggest a limit on the entry angle and a
    preferred rail when several are in range (**unverified**).
- **Catch box:** at setup each segment gets a bounding box grown by `max(RectSize.x, RectSize.y)` of its two nodes.
  The search only considers segments whose box contains the query position (plus a margin). Grind-rail nodes use a
  `RectSize` of 0.5–1.5.
- **Kart tuning in data:**
  - `PerformanceParamTable` has a `TopSpeedRailSlide` stat column: 0.95 at level 0, plus 0.005 per level, to 1.075 at
    level 25. Glide and wall ride have the same column; tarmac moves only ±0.003 per level.
  - `VehicleTrickSetGParam/TrickSetBase` has `OnRailSlideTrickCount1/2/3` = 40 / 300 / 600 (ground: 120 / 420 / 720,
    wall ride: 1 / 65 / 180). These are probably the charge thresholds for the trick boost levels while grinding
    (**unverified**).
  - The combo points (`VehicleGParam/Default` `ComboParam`) give `RailSlide` 400.
- Shells use rails too: the `ItemObjKoura*` parameter tables set `RailSlideVerticalDot`, `RailSlideVerticalDotSharp`
  and `WallCancelHeightRailSlide`.

## Object paths (`GuideGraphRail`)

These are not something the player rides. Actors link to one of their nodes (`BasicSignal`, sometimes
`MessageLink`) and follow the path. In DK Summit: `CmnObj_SnowBoarderShyGuy000_A_00` (the snowboarding Shy Guys),
`CmnObj_Eagle000_A_00`, `SystemObj_SkiLift_Mgr` and `SystemObj_ActorGeneratePoint_00`. Node `Dynamic`:
`VelocityScale` (0.1–10), `Param1`, `Param2`. Their meaning depends on the follower and is **unverified**. The
inspector lists the objects that use a selected path. Most object paths are curved (322 of 397, mostly cubic; see
"Curves"). Whether every follower moves along the curve rather than straight between nodes is not traced per actor.
