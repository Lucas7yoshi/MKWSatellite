# How a race course is assembled from scene layers

Verified against the **1.8.0** data and executable (2026-11-05). Function addresses are from the 1.8 `main` NSO
converted to ELF (base `0x7100000000`). "Verified" means confirmed in code *and* data. "Data only" means
consistent across all files but the controlling code was not traced.

## Summary

A race does **not** edit the open world. It is an *additive* stack of independent layers:

| Layer | Files | Contents | Loaded in a race? |
|---|---|---|---|
| Base world | `Banc/World_Game03/<RRCC>.bcett.byml.zs` (160 tiles) + `World_Game03/WorldResident.bcett.byml.zs` | All permanent scenery and **course furniture**: dash panels, glide ramps, boost rings, jump ramps, fences, decoration, world valley areas and respawn points | Yes (always-loaded world layer) |
| Free-roam world | `Banc/World_Game03-Free/<RRCC>.bcett.byml.zs` (142 tiles) + `-Free/WorldResident` | Free-roam only: ~4,600 coins, ~950 item boxes, P-switch mission gates, cameras, parking spaces, restart points | **No**: a separate layer loaded only in free-roam mode |
| Race scene | `Banc/Race_<Course>_<Variant>_NN[Game03].bcett.byml.zs` | Race-only content: `LapPathGraph` (checkpoints), AI `RoutePathGraph`, item boxes, coins, start grid, `RescueArea`, valley areas, audience, race fences | Yes |
| Off-world course layer | `Banc/<Course>_00_Fix.bcett.byml.zs` (only `Rainbow_00_Fix` today) | Rainbow Road's static furniture: its own dash plates, glide plates, boost/star rings, gates, high-speed areas, gravity/glide paths | Yes: it is referenced by the always-resident world scene (see below) |
| World AI paths | `Banc/Game03_AllPath_Fix.bcett.byml.zs` | 1,704 `RoutePathGraph` rails for the whole world (free-roam / CPU driving) | Not needed for course display |

## How the layers are authored (`RSDB/BancWorldTransformInfo`)

`RSDB/BancWorldTransformInfo.Product.NNN.rstbl.byml.zs` is the scene composition tree (3,828 rows, all tagged
`World_Game03`). Each row places a child scene (`Resource`) inside a parent scene (`id` = `<parent>?<guid>@World_Game03`)
with a transform and a nesting `depth`. Roots are the region scenes (`Forest_00_Fix`, `Plain_00_Fix`, `Ice_00_Fix`,
…, `World_Obj_00_Fix`, `World_Obj_Free_00_Fix`, `Auto_Game03_RoadTool`). Each region holds its course scenes and the
courses' free-roam sub-scenes, e.g. `Forest_00_Fix` contains:

- `Cinema_00` at (2255, 542, -4952), rotY -0.556; `BigTree_00`; `SFCMarioCircuit_00`
- `Cinema_Gnd_Free_00`, `Cinema_Obj_Free_00` (free-roam additions for the same area)

The shipped world tiles are this tree baked into absolute coordinates, then **partitioned by source** (data,
checked over every actor's `ParentPrefab`):
- base tiles contain **no** actor from any `*_Free*` prefab (88,829 actors: region `<Area>_Gnd_A_00` /
  `<Area>_Obj_00` prefabs, `Houdini/Auto_*` generated sets, manual placements);
- `-Free` tiles contain **only** `Prefab/<Area>_Obj_Free_00` actors plus `Houdini/Auto_AutoPlacementCoin` (10,124);
- 0 hashes are shared between the two.

## Which race uses what (`RSDB/Course`)

`RSDB/Course.Product.NNN.rstbl.byml.zs` has a row per playable layout (310, all `Scene` = `Game03`) with
`BancFile`, `GameRule`, `TrafficType`, `AudienceType`, `WorldConfigId` and a **world transform parent**:
- `[Game03]` race scenes have `WorldTransformParentFile` = their course scene (`Race_Cinema_A_00` → `Scene/Cinema_00`).
- Rainbow has `WorldTransformParentActor` = `SystemObj_PointRainbow_01`. That actor sits in the base world's
  `WorldResident` scene at (0, 0, 0) with no rotation, and its `Dynamic.PrefabPath` = `Scene/Rainbow_00`, which
  `SceneRes_LoadResolveFix` loads as `Rainbow_00_Fix`. So Rainbow's static layer is loaded **through the
  always-resident world**, and its parent transform is the identity.
- Race scenes **without** `[Game03]` are the SNES retro tracks added after 1.0, hosted inside a course area
  (developer names): `Cinema_B/C/D` = SFC Ghost Valley 1/2/3 (おばけぬま), `ChocoMountain_B/C` = Choco Island 1/2,
  `IceBuildings_B` = Vanilla Lake 1, `KoopaTroopaBeach_C` = SNES Koopa Beach 1. They have no transform parent and
  sit on the world map beside their host course (Ghost Valley next to the cinema at the same ground height).
  Also new since 1.0: `SFCMarioCircuit_A/B/C_01[Game03]`.
- `Race_DinoDinoJungle_B_00` (reverse) is listed in the table but has **no Banc file** in 1.8 (kept as data, not "fixed").

Coordinates: the placement parser multiplies every actor position by an optional parent matrix
(BancPlacement_ParseTransformMarkingScreen, `0x710029B9F0`). Race scene positions already line up with the baked world
(Boo Cinema's world dash panels are 9–18 units from its lap path; Rainbow's parent is the identity). So the effective
parent transform in shipping data is the identity, and the files can be drawn in their stored coordinates.

## Evidence that races don't edit the world

### No object-level overrides (data, all 57 race scenes)
- 0 race actors share a `Hash` or `InstanceId` with any of the 124,303 base-world objects (actors and rail points).
- 0 race `Links` point at world objects; links only ever target objects in the **same** scene.

### Course furniture lives in the base world (data)
Course-specific dash panels exist only in world tiles, e.g. `CmnObj_DashBoardCinema000_A/B/C_00` (Forest_00_Fix) and
`CmnObj_DashBoardSnowMountain000_A..E_00` (Ice_00_Fix): 9–33 units from their race's lap path. `Race_Cinema_A`
itself contains no dash panels.

### Code
- **World tile loader** WorldTile_RequestLoad `0x7100756F20`: `"%s/%s.bcett.json"`, or `"%s-Free/%s.bcett.json"`
  when the tile's mode field (`+0x30`) is 1. Base and Free are whole separate layers.
- **WorldResident loader** WorldLayer_LoadResident `0x7100756CD0`: `"%s/WorldResident.bcett.byml"` vs
  `"%s-Free/WorldResident.bcett.byml"` by the layer mode (`+0x78`).
- **Scene name resolution** SceneRes_LoadResolveFix `0x7100757260`: a requested scene `X` is loaded as `X_Fix`
  (or `X_Fix-Free`) when that file exists.
- **World manager** WorldMgr_LoadWorld `0x71005B6D80`: two world layers (manager `+0x50`, `+0x58`). The first is
  always loaded; the second only when GameScene_StartWorldLoad `0x71008A8440` passes `gameMode(+0x10C) == 0`, and
  WorldMgr_UpdateFreeLayerState `0x71005B12B0` additionally requires the `"GridFreeRun"` resource group. Mode 0 is
  the path that also uses the `"StageFreeRun"` resources (GameScene_UpdateWorldState `0x71005B3940`). Combined with
  the data partition above, the second layer is the `-Free` world and races don't load it.
  *Not traced:* the constructor that writes each layer's mode field, i.e. a direct proof that `+0x58` is mode 1.
- **Stage (race) scene loader** StageScene_LoadBanc `0x7100362670`: `"Banc/%s.bcett.byml"` with the full scene
  name (including `[Game03]`), independent of the world manager.

## Things in the data that change what is present at runtime

| Mechanism | What it does | Race impact |
|---|---|---|
| `Area_DeleteLinkedActor_00` | Developer label 「リンク先を削除するエリア（レインボー用）」: *area that deletes its link targets (for Rainbow)*. 13 placements (Rainbow race, `Rainbow_00_Fix`, Central→Rainbow route) with 682 `BasicSignal` links to the start section (station, start gate, solar panels, Thwomps, start dash/glide panels) and an audience group. | **Probably inert in 1.8.** Its `ActorGParam` has **no components** (no controller, no physics), only `BancDummyShape: Cube_c` for the editor, and the type name never appears in the executable. Nothing found that gives it behaviour; a generic link mechanism can't be ruled out. Shown, but not as a gameplay effect. |
| `SystemObj_CreateLinkArea_00` | Label 「生成リンクエリア」: *spawn-link area*. Real actor (controller `game::Obj_CreateLinkArea_Controller`, `IsSyncRaceFrame: true`, a Phive sensor box). **Traced in code (see below).** Targets of its `CreateLink` links are flagged at link resolution and **do not exist until a racer enters the area**; they then appear, staggered by distance, and are removed `ActiveSec` seconds after the last racer leaves. | Base world tiles (loaded in races): 95 areas, all linking to boost rings (`ActiveSec` 3, 10 or 30). On race paths: `Race_Central_A_00` 3, `Race_StormClouds_A_00` 2 (`1212`), `Race_SnowMountain_A_00` 2 (`WorldResident`, 30 s). The other 88 form a long ring run at Z ≈ 2870–3290 (tiles `0611`/`0711`/`0811`/`WorldResident`) that is on no race path; its west end is 140–400 units from `Race_WestCity_D_00`'s paths. `Race_DinoBones_A_00`: one glide boost ring (default 10 s). `Race_Central_C_00`: one with no links (does nothing). Free-roam (`-Free`): 41, mostly coins, coin bars and barrel cannons. |
| `Marking: ["RoadDeco"]` | The placement parser sets bit 0x08 of the placement flags (`+0x150`) when `Marking` contains `"RoadDeco"` (verified in code). 10,135 world actors: guardrails, reflectors, bollards, street lights, 296 dash panels from `Auto_Game03_RoadTool`. Reader of the bit not traced. | **None on race lines**: no RoadDeco dash/glide/jump object is within 40 units of any race's lap path. They decorate the open-world roads used by the routes between courses. |
| `ScreenSetting {Single, Double, Quadruple}` | Verified in code. Each placement has 4 mode bytes (`+0x148` Single, `+0x149` Double, `+0x14A` never written, `+0x14B` Quadruple), all starting at 1. Value 1 sets bit 0x2, value 2 sets bit 0x4; `Double` is also applied to the Quadruple byte. At creation the game picks the byte by screen count (1 → Single, 2 → Double, 3 or 4 → Quadruple) and **does not create the object if bit 0x2 is set**. With one screen the check is skipped entirely unless a global debug/config flag is set. No reader of bit 0x4 (value 2) was found. | **Value 1 = removed in that split-screen mode; no effect in single-player (time trials).** In 2P/3P/4P races, 705 race-scene objects disappear: audience, flags, Boos on Boo Cinema / Ghost Valley (15/9/5), snowboards on DK Pass (22), traffic cars on 17 race layouts (e.g. 11 on `MooMooFarm_A`, 16 on `WestCity_A`), oil drums. World: 802 (birds, seagulls, eagles, hologram signs). Value 2 (wooden fences etc., 200 world objects) has no known effect. |
| `SystemObj_ForbidObjAutoGenerateArea_00` | Label: *object auto-generation forbidden area*. 25, world only. | Affects auto-generated free-roam objects; none in race scenes. |

## What the viewer does with this
For a race scene, show:
1. the race scene (all of it), plus
2. `<Course>_00_Fix` if it exists, otherwise base-world tiles (`World_Game03`, never `-Free`) clipped to the 3D
   bounds of the race's lap/route paths plus 250 units. The clipping is **display only**: in game the whole base
   world is loaded; the box keeps unrelated parts of the map out of the view. The Y range keeps ground-level world
   objects out of Rainbow Road.

Every object keeps its source layer, its actor-database label, and incoming links ("deleted by area", "spawned by
area"), all shown in the inspector.

## Open questions (do not present as fact)
- Whether anything reads `Area_DeleteLinkedActor_00` at all (no components, no name in code).
- `CreateLinkArea`: which racers count (the stay-active check wants a contact whose flag `&2` is set; probably
  players, maybe CPUs) and the exact bit meanings of the target's spawn byte (`+0x151`).
- Who reads the RoadDeco flag and what it changes (only relevant off race lines).
- `ScreenSetting` value 2 (bit 0x4): no reader found.
- Dash panel facing: Boo Cinema's world dash panels face against the lap direction (see banc.md). Dash panels have
  **no controller** (`ActorGParam` components are only model, effect link, physics), so nothing can turn them at runtime.
  The boost comes from a Phive sensor body `<Type>_Zone` on layer `VehicleArea` (plus a `Ground` rigid body). The sensor
  params carry no direction, so the boost direction is probably decided by the kart code (likely the kart's own
  heading), which would make panel facing irrelevant. Not traced into the kart code; the `_Zone` shapes
  (`Phive/Shape/...`) are the real trigger footprint and worth drawing once Phive shapes are parsed.
- `SystemObj_RespawnPoint_00` (「リスポーン地点」, no components): the world-tile loader
  (`WorldTile_IndexRespawnPoints`, 0x710079B2A0) indexes every one into a 64×64 grid of 250-unit cells covering
  -8000..8000 in X/Z, in every mode (base world). The code that queries that grid was not found, so whether races use
  it for Lakitu is still open.
- `Banc/PathAdditionalData/<scene>.byml`: per-point precomputed data in hash-map nodes
  (keys `CH`, `CHHit`, `CHNum`, `SD`, `ST`, `VDP`, `WDM`, `HS`). Not decoded.

IDA names given in the 1.8 database: `WorldTile_RequestLoad` (0x7100756F20), `WorldLayer_LoadResident` (0x7100756CD0),
`SceneRes_LoadResolveFix` (0x7100757260), `WorldLayer_UpdateTileLoads` (0x71005B4460), `WorldMgr_LoadWorld`
(0x71005B6D80), `WorldMgr_UpdateFreeLayerState` (0x71005B12B0), `GameScene_StartWorldLoad` (0x71008A8440),
`GameScene_UpdateWorldState` (0x71005B3940), `StageScene_LoadBanc` (0x7100362670), `RailGraph_ParseRail`
(0x710029AD70), `BancPlacement_ParseTransformMarkingScreen` (0x710029B9F0), `WorldTile_IndexRespawnPoints`
(0x710079B2A0), the CreateLinkArea functions in the section below, `WorldTile_CreateCollects_ScreenFiltered` (0x710079A0E0), `Placement_UpdateScreenSettingHidden`
(0x71009FC1F0); ScreenSetting checks are also commented at 0x7100DBF61C, 0x7100AFB080, 0x71007F308C.

## `SystemObj_CreateLinkArea_00` (traced in 1.8)
Controllers are created through a factory keyed by **32-bit MurmurHash3 (seed 0) of the `CtrlName`** (here
`0x7433595B`, registered at 0x7100EBB390, creator 0x7100E4A740, vtable 0x71035CCB98). `Dynamic` keys are read by the
same hash. Gyml `$type`s use a seeded MurmurHash64A instead (e.g. `game__ObjectGParam`).

1. **Link resolution** (`BancLinks_ResolveTargets`, 0x71002A1730): a link named `CreateLink` (mm3 `0x98AE835B`) sets
   bits in the target placement's spawn byte `+0x151` so the target is not created with its tile.
2. **Trigger** (`CreateLinkArea_OnEnter`, 0x7102238610, vtable +0x2E8; ignored while already active): for every
   linked target it takes the distance from the entering racer, then delay = `WaitFrame` +
   `MaxDelayFrame` × (d − d<sub>min</sub>) / (d<sub>max</sub> − d<sub>min</sub>) frames (nearest first; both default 0, so
   without them everything appears at once). Targets of type `CmnObj_Coin001_A_00` also get an extra tracker
   object. Then `CreateLinkArea_StartTimer` (0x7102238A40): timer = `ActiveSec` × 60 frames, **default 600 (10 s)**.
3. **Per frame** (`CreateLinkArea_SpawnDue`, 0x7102238500): each target whose delay has passed gets its spawn
   byte set.
4. **Update** (`CreateLinkArea_Update`, 0x7100EC2C00): while a qualifying racer is still inside, the timer does not
   run; otherwise it counts down. At 0 (and with no tracked `Coin001` object within 200 units) it deactivates and
   the targets are removed. It also sends a signal (`0x0BA869BB`, name unknown) on activate/deactivate, which is
   presumably what `BasicSignal` targets (12 links: rings, barrel cannons) react to.

So for runners: a boost ring linked from one of these areas is **not there** on approach until someone drives
into the area, and on later laps it is only still there if someone touched the area within `ActiveSec`.
