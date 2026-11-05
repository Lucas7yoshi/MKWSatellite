# Roadmap

Goal: show Mario Kart World course data accurately for speedrunners and time trialers, in a viewer that can grow into a basic editor.
Visual reference: the Mario Kart 8 Deluxe course editors (Track Studio / MapStudio): ImGui outliner + 3D viewport + property panel, with paths and checkpoint gates drawn in 3D.

## Plan (agreed 2026-11-05)

Work in this order, then stop for review.

1. **Course data model**
   - `mkwsat scan`: parse every Banc scene and report failures.
   - Typed objects: lap path + checkpoint gates (`RectOffset`/`RectSize`), respawn / valley / glide-kill areas, dash panels, glide ramps, boost rings (with facing), item boxes, coins, AI routes, gravity/glide paths.
   - Check field meanings against the game code (IDA) before presenting them as fact.
2. **Viewport + 2D view**
   - Silk.NET OpenGL viewport with an orthographic top-down camera (real geometry, so 3D drops in later).
   - Layer toggles, picking + ImGui inspector, distance ruler, scene search.
3. **Models**
   - Stage BFRES (v10) + BNTX textures rendered in the same viewport; perspective camera.
   - Check course texture formats early (Switch 2 may use ASTC).
   - Needs the project licence decided first (see DECISIONS.md).

Phive collision: done 2026-11-05 (formats/collision.md). Editing and saving: started 2026-11-05 (race layer; see D17).

## Progress log

| Date | Done |
|---|---|
| 2026-11-04 | Skeleton: Formats (zstd, BYML, layered romfs, Banc scenes), App (Silk.NET + ImGui placeholder), CLI (`info`, `banc`), tests. |
| 2026-11-04 | Confirmed base dump is 1.0.0; extracted a 1.8.0 dump (see formats/nsp.md). 1.8 has 717 Banc scenes (57 `Race_*`) vs 693 (47) in 1.0. |
| 2026-11-05 | Roadmap agreed; docs/ started. |
| 2026-11-05 | Step 1 (course data): `mkwsat scan` parses all 717 Banc scenes in 1.8 with 0 failures; BYML hash-map nodes (0x20/0x21) added (needed for `Banc/PathAdditionalData`). New CLI: `dump` (any BYML as a tree), `course` (assembled course summary + orientation checks). |
| 2026-11-05 | Verified course layering in data and code (formats/scene-layers.md): race scene + base world tiles (never `-Free`) or `<Course>_00_Fix`. `CourseLoader`/`CourseScene` model with layers, categories, lap points with gates. Rotation convention and gate rectangle checked against RailGraph_ParseRail. |
| 2026-11-05 | Step 2 (2D view): GL top-down viewport (PrimitiveBatch + TopDownCamera), height-coloured lap path, checkpoint gates + labels, AI routes, glide/gravity rails, items/coins/boosts/start grid/hazards, areas, layer toggles with legend, click-to-inspect, hover tooltips, right-drag ruler, scale bar, `--course/--screenshot/--size` for headless images. **Stopped for review before step 3 (models).** |
| 2026-11-05 | Layering deep-dive (user: critical): RSDB `Course` + `BancWorldTransformInfo` + `ParentPrefab` partition confirm base/-Free/race/`_Fix` layering; Rainbow's `_Fix` is loaded via `WorldResident`'s `SystemObj_PointRainbow_01`; `Area_DeleteLinkedActor` = Rainbow start-section unloading; RoadDeco flag verified in code and absent from race lines; `ScreenSetting` split-screen flags; SNES retro tracks identified. Actor database (`RSDB/Actor`) labels now drive classification (fixed: Rainbow's own 23 dash plates/5 glide plates were "Other"; `GlideAreaKiller` is a Bullet-Bill glide area, not a kill area). New categories: recovery points, invisible dash/glide areas, rule areas, link areas. |
| 2026-11-05 | Traced ScreenSetting (value 1 = not spawned in that split-screen mode, ignored in 1P), dash panels (static, sensor-zone boost, no runtime flip possible), `Area_DeleteLinkedActor` (no components: likely inert), world `RespawnPoint` grid index. Actor gyml params documented. |
| 2026-11-05 | Traced `CreateLinkArea`: controller factory key = MurmurHash3 of `CtrlName`; `CreateLink` targets (mostly boost rings) are absent until a racer enters, appear staggered by `MaxDelayFrame`, vanish `ActiveSec` (default 10 s) after the area is left. 7 areas on race paths (Central A, Storm Clouds, Snow Mountain) plus Dino Bones' race-scene one. Inspector explains it. |
| 2026-11-05 | Step 3 (models): managed SARC/gyml model resolver, BFRES v10 reader, BNTX 4.1 reader, Switch 2 block-linear unswizzle (GOB layout differs from Switch 1), ASTC + BC1-5 decoders. App: models drawn under the overlay in top-down and in a new 3D orbit view (WASD/QE fly), background loading, `--3d`/`--orbit`/`--no-models`. CLI: `tex`, `model`. Not done: terrain (TerrainArc), BC6H/BC7, game-accurate shading. |
| 2026-11-05 | Review tweaks (user): 3D overlay uses camera-facing ribbons sized in pixels (AI routes and paths were invisible edge-on); checkpoint gates drawn as upright rectangles in 3D, thicker top-down; areas are 3D boxes (vertical origin from the editor `BancDummyShape`: `Cube_t` sits on its base, `_c` centred) and selectable in 3D by ray test; AI routes coloured by `RoutePriority` / Bullet-Bill-only flags with direction chevrons; UI scale from monitor height with a crisp font (`--ui-scale`), line/marker and text size sliders. |
| 2026-11-05 | UI scale now follows Windows' per-monitor display scale (DPI / 96) like a normal app: the window opens at size × scale and resizes and re-rasterises its font when moved to another monitor. Screenshots stay at scale 1 unless `--ui-scale`. |
| 2026-11-05 | Bullet Bill no-finish zones traced (formats/banc.md): `IsForbidKillerFinish` on a route point holds until the next point; plus a turn/look-ahead check in code. Viewer draws red no-finish stretches and green `IsKillerReleasable` points (toggle). Path node dots, larger pick radius, screen-space hover/selection rings, hand cursor. Interstage and Rally scenes listed. |
| 2026-11-05 | Bullet Bill look-ahead decoded: no finish within 30 units of a point turning > 0.45°/unit (or `ItemType` 1/2), or 30-50 units before one turning > 2°/unit. `BulletBillRules` + tests; drawn in orange next to the red data flags. |
| 2026-11-05 | Editing (user): BYML writer (round-trips all 717 Banc scenes), zstd compression, `BymlEditHistory` undo/redo, race-layer rebuild after edits, inspector fields for position/rotation/scale and `Dynamic` values (add/remove bool keys), Ctrl+drag to move, Ctrl+Z/Y/S, save/revert with unsaved-changes prompt, `modOutput` setting. Not done: adding/deleting actors or rail points, gate size editing, resource size table update. |
| 2026-11-05 | Placement fixes (user reports): lap gates centred at RectOffset / 2 (checked against the stage model with `mkwsat ground`); areas use their Phive physics box (2 x Scale, `Cube_t` hangs below the origin), checked with `mkwsat areafit` (Rainbow high-speed areas now contain the path: 18/18, was 2/18). |
| 2026-11-05 | 3D overlay depth option "Behind geometry": draw on top / dim (default) / hide, depth-tested against the models with a small pull towards the camera so road-level lines stay visible. |
| 2026-11-05 | Lap progress traced (user test showed drawn gates were wrong): gates span RectOffset ± RectSize (half-extents, full offset), progress = segment between two gate planes within interpolated width/height; new "Lap progress zones" layer. |
| 2026-11-05 | Screenshot options `--no-ui` (map only) and `--layer Field=0|1` (any ViewLayers toggle). User confirmed in game that the traced gates/progress zones match. |
| 2026-11-05 | Tiled top-down screenshots: `--no-ui --size WxH` larger than the window renders window-sized tiles at one scale and stitches them (e.g. 6000x6000). `--layer` also sets float fields (`LineScale=2`). |
| 2026-11-05 | Terrain (formats/terrain.md): TSCB quadtree + TARB maps decoded (height, material, color_mul2, albedo_far, water), checked against lap paths and the game's own far albedo. App streams it by camera (frustum + screen-space LOD, worker loads, 320-tile GPU cache), materials from the 46-layer albedo array, water surfaces; "Terrain"/"Water" toggles, frame time in the status line. CLI `terrain`, `tex --layer`. Not done: terrain normal maps, `bake_*` lighting, Phive terrain collision. |
| 2026-11-05 | Merged world geometry (`Piece_Gridify_*`, terrain.md): Crown City's streets, roads and bridges between courses now drawn (they were missing before terrain too). Water only where clearly above the ground; tiled screenshots wait for streaming per tile. CLAUDE.md added (working rules, IDA). |
| 2026-11-05 | Rails (formats/rails.md): grind rails drawn (RailSlidePath by RailSlideType, ColRailSlidePath on fences/guardrails), all other path kinds drawn; inspector explains each kind, lists rail length, prefab, owning actor and the objects that follow it; course summary has a Paths table. Snowboarding Shy Guys follow GuideGraphRails. |
| 2026-11-05 | Collision (formats/collision.md): Havok tagfile reflection reader, Phive shapes/static compounds (quantized + float mesh sections, per-triangle material runs, nested compounds, convex hulls, capsules), PhiveConfig names. Course collision = race-scene compound + world compound cells + terrain cells, or `_Fix` stage shapes via ShapeParam. Viewer "Collision" layer coloured by material or surface, volumes, per-material hiding, hover info. CLI `collision` (lap nodes sit on the collision: median 0-0.9 units on four courses). Not done: kart/character hitboxes. |
| 2026-11-05 | Vehicle hitbox data located (collision.md, unverified): `CustomizerVehicleInfoGParam` `LocalCollisionBox` + tyre sizes; CLI `vehicles`. Not yet traced in code or drawn. |
| 2026-11-05 | Collision layer: highlight one volume material (default Rescue: x-ray, light edges), volume opacity slider, volumes keep material colour in surface mode. |
| 2026-11-05 | Rescue/Valley collision boxes = RescueArea/ValleyArea actors baked into the race compound (all matched on 4 courses); `Rescue` surfaces on ground geometry are a separate case (collision.md). |
| 2026-11-05 | PNG writer: RGB when opaque, per-row adaptive filters, zlib SmallestSize (4K screenshot 16.7 → 10.7 MB, lossless). |
| 2026-11-05 | Camera framing (top-down and 3D) includes every lap gate, so progress zones aren't cropped. |
| 2026-11-05 | `mkwsat dump <path> --yaml`: whole BYML as plain YAML. |
| 2026-11-05 | Non-checkpoint lap gates drawn orange and thicker (were faint grey). |
