# MKWSatellite docs

Working notes kept alongside the code: what's done, why things are the way they are, and what we know about the game's file formats.

| File | Contents |
|---|---|
| [ROADMAP.md](ROADMAP.md) | Agreed plan and progress log |
| [DECISIONS.md](DECISIONS.md) | Decision log (what was chosen, alternatives, why) |
| [formats/romfs.md](formats/romfs.md) | Game dump layout, versions, updates, where things live |
| [formats/byml.md](formats/byml.md) | BYML container format as implemented by our reader |
| [formats/banc.md](formats/banc.md) | Banc course placement scenes (actors, rails, checkpoints) |
| [formats/scene-layers.md](formats/scene-layers.md) | How a race course is assembled from scene layers (race scene, base world, `_Fix`); verified in code |
| [formats/rsdb.md](formats/rsdb.md) | RSDB resource databases (actor labels, course table, world scene tree) |
| [formats/nsp.md](formats/nsp.md) | NSP/NCA notes from extracting a game dump |
| [formats/models.md](formats/models.md) | Actor → model links, BFRES 10, BNTX 4.1, Switch 2 texture swizzle, the model viewer |
| [formats/rails.md](formats/rails.md) | Rail kinds: grind rails (types, flags, catch boxes), object paths, glide and other paths |
| [formats/collision.md](formats/collision.md) | Phive/Havok collision: tagfile reflection, mesh/compound shapes, materials, which files make up a course |
| [formats/out-of-bounds.md](formats/out-of-bounds.md) | Lakitu rescue: triggers (Rescue/Valley material, WorldEndArea, ForbidReverse, stuck watchdog), respawn point choice, OOB data per course |
| [formats/terrain.md](formats/terrain.md) | Open-world terrain: TSCB quadtree, TARB height/material/colour/water maps, how the viewer streams it |

Conventions:
- Mark anything not confirmed against game code or data as **unverified**.
- Use romfs-relative paths (e.g. `Banc/...`), never paths from someone's machine.
- Game version matters: say which version (`Product.100` = 1.0.0, `Product.180` = 1.8.0) a finding came from.
