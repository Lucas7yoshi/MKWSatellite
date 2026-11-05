# Decisions

Newest last. Each entry: decision, alternatives considered, reason.

### D1. C# on .NET 10 (2026-11-04)
User's choice. .NET 10 is the current LTS.

### D2. Silk.NET (OpenGL) for rendering, Dear ImGui for UI (2026-11-04)
- Alternatives looked at: NeoVeldrid (maintained Veldrid fork), Stride 4.4, Godot 4.6 C#, MonoGame 3.8.5, OpenTK, Vortice.Windows, WebGPU bindings.
- Silk.NET chosen by the user; Vortice ruled out (used elsewhere, Windows-only). ImGui suits an inspector-heavy tool.
- Start on OpenGL 4.1 core (works everywhere incl. macOS); Vulkan possible later through Silk.NET.

### D3. Format code lives in its own library (2026-11-04)
`MKWSatellite.Formats` has no UI/GPU dependencies so it can back the app, the CLI and tests, and later an exporter.

### D4. Never commit machine paths, game data or keys (2026-11-04)
Game location comes from `--romfs`, `MKWSAT_ROMFS`, or a git-ignored `mkwsat.local.json`. `.gitignore` blocks game file types and `*.keys`. Build outputs use `PathMap`.

### D5. Respect licences; no Switch Toolbox code unless the project goes GPL (2026-11-04)
Switch Toolbox (and likely Track Studio / MapStudio) is GPL-3.0. Reading it as a format reference is fine; copying code requires a GPL-3.0-compatible project licence. **Project licence: not chosen yet** (needed before model work).

### D6. Romfs is layered (2026-11-04)
`GameFileSystem` takes base + update romfs directories, later ones win, matching how game updates work. A fully extracted update (like our 1.8 dump) is used on its own as a single layer.

### D7. Keep raw data on every parsed object (2026-11-04)
Each Banc actor/rail/point keeps its original BYML node, so nothing is lost and saving back can be added without a redesign.

### D8. Roadmap order: data model → 2D view → models, then stop (2026-11-05)
User's call. Collision and editing deferred.

### D9. The 2D view is a real GL viewport with an orthographic camera (2026-11-05)
Instead of ImGui draw-list lines. Slightly more work up front, but paths, gates and objects become geometry that the later 3D/model view reuses unchanged.

### D10. A course = race scene + one static layer, never the free-roam world (2026-11-05)
Verified in data and code (formats/scene-layers.md). The static layer is `<Course>_00_Fix` if it exists, else base-world
tiles clipped to the race paths' 3D bounds (+250 units). Every object keeps its source layer so the UI can filter it.
Alternatives (all world objects, or the race scene only) would clutter the view or miss the course's dash panels.

### D11. Display categories are name-based; behaviour claims need code checks (2026-11-05)
`CourseObjectClassifier` maps type names to categories for colour/toggles only. Anything a runner might rely on
(facing, area sizes, what an area does) is labelled unverified in the UI/docs until traced in the 1.8 executable.

### D12. Screenshots come from the real app (2026-11-05)
`MKWSatellite --course <name> --screenshot out.png` renders the normal UI for a few frames and saves the back buffer
(own PNG writer, no dependency). One rendering path for interactive and image use. Screenshots are derived from
game data and are not committed.

### D13. Classify by the developers' labels, and never "fix" the game's data (2026-11-05)
`RSDB/Actor` labels (e.g. 「汎用ダッシュ板」 = general dash board) are more reliable than type names, so they drive the
category, with names as a fallback. Corrections are only made to **our** interpretation (e.g. a keyword rule that
misread a label). Oddities in the game's own data (reversed dash panels, a course row with no file, unused objects)
are shown as stored and noted, because runners need what the game actually has (user's direction).

### D14. Models and textures use our own managed code (2026-11-05)
User's direction: prefer C# over native libraries. BFRES, BNTX, SARC readers, the block-linear unswizzle and the
ASTC/BC decoders are written in C# in `MKWSatellite.Formats` (ASTC from the Khronos spec). No Switch Toolbox code was
copied, so D5's licence question does not block this step. A decompiler for reading Toolbox's DLLs was not used.

### D15. Switch 2 swizzle found by search, then confirmed visually (2026-11-05)
The Switch 1 GOB layout produced scrambled textures. Rather than guess, every ordering of the GOB's address bits was
scored on image smoothness; the winner is documented in formats/models.md and covered by a unit test.

### D16. Albedo-only, capped-mip textures in the viewer (2026-11-05)
The goal is readable geometry for runners, not the game's look: one directional light, albedo, alpha cutout. Textures
above 1024 px use a smaller stored mip (less VRAM, faster decoding). The 2D overlay draws on top of models (no depth
test) so paths stay visible inside covered stages.

### D17. Editing: race layer only, saved to a separate mod folder (2026-11-05)
Only the race scene (`Race_*.bcett`) is editable; world tiles and `_Fix` layers are shared with other courses or
free roam and stay read-only. Edits change the parsed BYML tree in place through `BymlEditHistory` (one key per step,
whole values replaced, so undo restores references), then only the race layer is rebuilt. Saving writes
`<mod folder>/Banc/<scene>` (zstd, power-of-two padded) and refuses any folder inside a game dump (a layer with
`System/Resource`). Adding the mod folder as the last `romfs` layer loads the edits back. The resource size table is
not rewritten; growth past the original padded size is reported instead.
