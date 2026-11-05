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
