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
inspector lists the objects that use a selected path.
