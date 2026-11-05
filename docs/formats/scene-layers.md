# How a race course is assembled from scene layers

Verified against the **1.8.0** data and executable (2026-11-05). Function addresses are from the 1.8 `main` NSO
converted to ELF (base `0x7100000000`). "Verified" means confirmed in code *and* data. "Data only" means
consistent across all files but the controlling code was not traced.

## Summary

A race does **not** edit the open world. It is an *additive* stack of independent layers:

| Layer | Files | Contents | Loaded in a race? |
|---|---|---|---|
| Base world | `Banc/World_Game03/<RRCC>.bcett.byml.zs` (160 tiles) + `World_Game03/WorldResident.bcett.byml.zs` | All permanent scenery and **course furniture**: dash panels, glide ramps, boost rings, jump ramps, fences, decoration. Region-authored (`AreaName` = `Scene/Forest_00_Fix.bcett.json`, `Plain_00_Fix`, …, plus auto-generated `Auto_*` layers). | Yes (always-loaded world layer) |
| Free-roam world | `Banc/World_Game03-Free/<RRCC>.bcett.byml.zs` (142 tiles) + `-Free/WorldResident` | Free-roam only: ~4,600 coins, ~950 item boxes, P-switch mission gates, cameras, parking spaces, restart points. | **No**: separate layer, loaded only in free-roam conditions |
| Race scene | `Banc/Race_<Course>_<Variant>_NN[Game03].bcett.byml.zs` | Race-only content: `LapPathGraph` (checkpoints), AI `RoutePathGraph`, item boxes, coins, start grid, respawn (`RescueArea`), valley/kill areas, audience, race fences | Yes |
| Off-world course layer | `Banc/<Course>_00_Fix.bcett.byml.zs` (only `Rainbow_00_Fix` today) | The static furniture of a course outside the world grid (Rainbow Road: boost rings, coins, gravity/glide paths, lap gate) | Yes, for that course (data only, see below) |
| World AI paths | `Banc/Game03_AllPath_Fix.bcett.byml.zs` | 1,704 `RoutePathGraph` rails for the whole world (free-roam / CPU driving) | Not needed for course display |

## Evidence

### No object-level overrides (data, all 57 race scenes)
- 0 race actors share a `Hash` or `InstanceId` with any of the 124,303 base-world objects (actors and rail points).
- 0 race `Links` point at world objects.
- 0 hashes are shared between base world tiles and `-Free` tiles.

So nothing in a race scene replaces, moves or deletes a world object by reference. The theory "the world is altered
by the course layer" does not hold at the data level; any altering would have to come from runtime rules
(see Open questions).

### Course furniture lives in the base world (data)
Course-specific dash panels exist **only** in world tiles, e.g. `CmnObj_DashBoardCinema000_A/B/C_00`
(`AreaName` Forest_00_Fix) and `CmnObj_DashBoardSnowMountain000_A..E_00` (Ice_00_Fix). Distance from these world
actors to the race's own lap path:
- Boo Cinema (`Race_Cinema_A_00[Game03]`): dash panels 9–18 units, glide board 2 units.
- Snow Mountain: dash panels 21–33, boost rings 13–53, glide boards 3–12.

`Race_Cinema_A` itself contains **no** dash panels, so without the world layer the course would be missing them.

### Rainbow Road uses `Rainbow_00_Fix`, not the world (data only)
World furniture is ~2,400+ units from Rainbow's lap path (it sits far below). `Rainbow_00_Fix` objects are right on
it: coins median 21, `StarRing` 30, `AccelRing` 54, and the lap gate 8 units away. It also carries the course's
`GravityPath` / `GlidePath` rails. How the game chooses `Rainbow_00_Fix` was not traced.

### Code
- **World tile loader** `0x7100756F20`: formats `"%s/%s.bcett.json"` or, when the layer's mode field (`+0x30`) == 1,
  `"%s-Free/%s.bcett.json"`. So base and Free are whole separate layers, never merged per object.
- **WorldResident loader** `0x7100756CD0`: `"%s/WorldResident.bcett.byml"` vs `"%s-Free/WorldResident.bcett.byml"`
  by the layer mode (`+0x78`).
- **Scene name resolution** `0x7100757260`: a requested scene `X` (from `.bcett.json` names) is loaded as `X_Fix`
  (or `X_Fix-Free` for names ending `-Free`) when that file exists. This explains the `_Fix` names used in
  `AreaName`/`RelativeFiles`.
- **World manager** `0x71005B6D80`: holds two world-layer objects (manager `+0x50` and `+0x58`). It always loads
  the first, and loads the second only when its flag is set. The caller `0x71008A8440` passes
  `flag = (gameModeField +0x10C == 0)`, and `0x71005B12B0` gates the second layer on the `"GridFreeRun"` resource
  group. The second layer is therefore the free-roam (`-Free`) one, loaded only in that mode.
  **Not yet confirmed:** the constructor that sets each layer's mode field (to prove `+0x58` is mode 1).
- **Stage (race) scene loader** `0x7100362670`: loads `"Banc/%s.bcett.byml"` with the full scene name
  (including `[Game03]`). It is independent of the world manager, so the race scene is a separate layer on top.

IDA names given in the 1.8 database: `WorldTile_RequestLoad` (0x7100756F20), `WorldLayer_LoadResident` (0x7100756CD0),
`SceneRes_LoadResolveFix` (0x7100757260), `WorldLayer_UpdateTileLoads` (0x71005B4460), `WorldMgr_LoadWorld`
(0x71005B6D80), `WorldMgr_UpdateFreeLayerState` (0x71005B12B0), `GameScene_StartWorldLoad` (0x71008A8440),
`GameScene_UpdateWorldState` (0x71005B3940), `StageScene_LoadBanc` (0x7100362670).

## What the viewer does with this
For a race scene, show:
1. the race scene (all of it), plus
2. the static course layer: `<Course>_00_Fix` if it exists, otherwise base-world tiles (`World_Game03`, never
   `-Free`) clipped to the 3D bounds of the race's lap/route paths plus a margin. The Y range matters: it keeps
   ground-level world objects out of sky courses.

Each object keeps its source layer so the UI can colour or filter by layer.

## Open questions (do not present as fact)
- Runtime suppression of world objects during races: `Area_DeleteLinkedActor_00` (Rainbow race and Fix scenes),
  `SystemObj_ForbidObjAutoGenerateArea_00`, `SystemObj_CreateLinkArea_00`, and world actors with
  `Marking: ["RoadDeco"]` (10,135 of them, incl. 296 dash panels from `Auto_Game03_RoadTool`). Not traced.
  RoadDeco dash panels may be free-roam road decoration that is or is not active in races.
- Dash panel facing: Boo Cinema's world dash panels face against the lap direction (see banc.md). Possibly flipped
  at runtime by travel direction (user's hypothesis).
- Why some race scenes lack the `[Game03]` suffix (`Race_Cinema_B/C/D_00`, `Race_ChocoMountain_B/C_00`, …).
- `Banc/PathAdditionalData/<scene>.byml`: per-point precomputed data in hash-map nodes
  (keys `CH`, `CHHit`, `CHNum`, `SD`, `ST`, `VDP`, `WDM`, `HS`). Possibly checkpoint data per route point; not decoded.
