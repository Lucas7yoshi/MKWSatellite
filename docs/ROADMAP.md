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

Deferred: Phive collision (surface types), editing and saving (BYML writer).

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
