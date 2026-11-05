# Game dump layout (romfs / exefs)

## Versions
- `System/Resource/ResourceSizeTable.Product.NNN.*.rsizetable.zs`: NNN is the content version (`100` = 1.0.0, `180` = 1.8.0). Other files carry it too (e.g. `Sound/Bgm/bgm.Product.100.byml.zs`).
- `mkwsat info` reports it (`GameFileSystem.GetResourceVersion`).
- Updates ship as a separate romfs holding only changed files; when extracted with the base applied (as with our 1.8 dump) the result is a complete romfs.

## Size and access
~206k files, ~22 GiB (1.8). Never recurse the whole tree from code or tools; list specific directories (`GameFileSystem.ListFiles` is deliberately non-recursive). Most files are zstd-compressed (`.zs`).

## Where things live
| Directory | Contents | Status |
|---|---|---|
| `Banc/` | Course/scene placement, `*.bcett.byml.zs` (see banc.md). 1.8: 717 scenes, 57 `Race_*`. | Parsed |
| `Model/` | BFRES models, `<Group>.<Model>.bfres.zs` (e.g. `Stage_Cinema.Stage_Cinema_A.bfres.zs`, `..._Far`). ~5.8k files. | Planned |
| `Tex/` | External textures for models | Planned |
| `Phive/StaticCompoundBody/Course/` | Per-scene collision, `*.Nin_Ounce_NVN2.bphsc.zs` (e.g. `Race_Cinema_A_00`, `RaceStage_Cinema_A_00`) | Deferred |
| `Phive/Shape/Dcc/` | Collision shapes `*.bphsh.zs` (e.g. `Stage_Cinema_A`) | Deferred |
| `TerrainArc/Game03_Proc.tscb`, `TerrainArc/Game03_Proc/` | Open-world terrain quadtree and per-area maps (see terrain.md) | Drawn (height, materials, colour, water) |
| `Phive/Terrain/` | Terrain collision | Not planned |
| `Sound/Resource/` | Audio (`*.bars.zs`, BARS/AMTA with music metadata) | Out of scope |

## exefs
`main` (game code, NSO), `sdk`, `subsdk0..1`, `rtld`, `main.npdm`. 1.8's `main` is ~3.5 MiB larger than 1.0's; function addresses differ between versions.
