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
