# Terrain (`TerrainArc/`)

The open world's ground (fields, beaches, the dirt track of Moo Moo Meadows, the ground under Crown City) is not in the
`Stage_*` models; it is a height-map terrain streamed from a quadtree. Everything below was read from the 1.8
(`Product.180`) data and checked against course paths; nothing here is traced in code yet unless stated.

Files:
- `TerrainArc/Game03_Proc.tscb`: the scene: header, material table, resource types and the quadtree of areas.
- `TerrainArc/Game03_Proc/<area>.<ext>`: per-area maps (43,458 files in one folder; open by exact name, never list
  it on a slow disk).
- `Tex/CmnTex_Tera_Product_Proc_Alb.bntx.zs` / `_Nrm`: the material textures, one ASTC 8×8 array of 46 layers
  (1024² albedo, 1434² normal).
- `Model/TeraModel_Product_Proc.TeraTerrain.bfres.zs`: the terrain shader materials (near/middle/far), not needed to draw.

## TSCB (magic `TSCB`, version 0x90C)

Same family as Breath of the Wild's TSCB, but every offset is **relative to the field that holds it**.

| Offset | Type | Meaning |
|---|---|---|
| 0x10 | f32 | World scale: area units → world units (250) |
| 0x14 | f32 | Max height: height map value 1.0 → world units (2000) |
| 0x34 | u32 | Material count (62) |
| 0x38 | u32 | Area count (6448) |
| 0x70 | u32 | Resource type count (12) |
| 0x74 | rel | Material table (array of rel offsets) |
| 0x78 | rel | Area table (array of rel offsets) |
| 0x7C | rel[] | Resource type entries |

**Resource type** (0x80 bytes): u32 id at 0, rel offsets to the name at 0x60 and the file extension (or class) at 0x64.
Ids: `0x0000` height (`hght`), `0x0100` normal (`norm`), `0x2000` material (`mate`), `0x4100` water, `0x4400` road,
`0x4500` dirt, `0x4700` color_mul2, `0x4800` biome, `0x4900` biome_vol, `0x4A00` albedo_far, `0x4B00` bake_light,
`0x4C00` bake_gi. Road, dirt, biome and biome_vol are used by no area in Game03. The tile folder also has `sky_bounce`
and `slow` maps that the table does not list.

**Material** (0x3C bytes): u32 albedo/normal array layer at 0; f32 UV scale at 0x10/0x14 (0.05 = repeats every 20 world
units; one material uses 0.1); f32 RGB at 0x30..0x38 (a grey or tinted base colour; not needed to reproduce the far
albedo, see below).

**Area**: f32 x, z (centre) and size at 0/4/8, in area units; u32 resource entry count at 0x20; rel offset to the
name at 0x60; then one rel offset per resource entry from 0x68. A resource entry (0x3C bytes) starts with the type id;
for the height entry, f32 at 0x20/0x24 are the area's min/max height as fractions of the max height.

The areas form a quadtree: level 0 is one 16000-unit square centred on the origin, each level halves the size,
level 7 squares are 125 units. Levels 0-4 are complete (1+4+16+64+256 areas); below that the tree is **sparse**
(538, 1926, 3643 areas) and only refined where needed, and a node can have fewer than four children. Area names are a
level digit plus a hex index: `6<level><index>` (e.g. `6700003C4A`).

## Per-area maps

Each file is a `TARB` wrapper: magic, then u16 header size at 0x0A (0x14) and u32 decompressed size at 0x0C, then one
zstd frame. The payload is a 0x34-byte surface header (component layout codes such as `09 00 00 00` for one 16-bit
channel, `06 06 06 06` for RGBA8, `06 16 06 06` for BC3) whose last u32 (0x30) is the surface size; the surface is the
last that many bytes of the payload. Height maps carry another block before it, not needed for drawing. Surfaces
are block-linear (Switch 2 GOB layout, see models.md) with a block height of one GOB.

| Map | Surface | Meaning |
|---|---|---|
| `hght` | 260×260 u16 | Height: value / 65535 × max height |
| `mate` | 260×260 RGBA8 | Material A, material B (indices into the material table), blend A→B (0..255), unused |
| `color_mul2.extm` | 260×260 BC3 (sRGB) | Colour multiplier for the material textures (alpha ~0.5, meaning unknown) |
| `albedo_far.extm` | 260×260 BC3 (sRGB) | Precomputed albedo, levels 0-4 only (341 areas) |
| `water.extm` | 65×65 × 4 u16 | Channel 0: water surface height (same scale as `hght`); 1/2 ~0.5 (flow, **unverified**); 3: small type value (**unverified**) |
| `norm`, `bake_*`, `sky_bounce`, `slow` | various | Not used by the viewer |

Grid: texel *i* sits at `area min + i × size / 256`, so texels 0..256 span the area and 257..259 repeat the first
columns of the next area (checked: column 256 of an area equals column 0 of its +X neighbour, row 256 equals row 0
of its +Z neighbour). Rows run along +Z, columns along +X. Water texel *i* sits at `min + i × size / 64`.

Checks against course data (`mkwsat terrain <course>`):
- Height: Moo Moo Meadows' lap path (a terrain track) sits a median 0.9 units above the sampled terrain, 80% of points
  within 3 units. With rows and columns swapped the fit is clearly worse.
- Colour: for level-4 areas, `texture(layer) × srgb_to_linear(color_mul2)` reproduces `albedo_far` with a fitted scale
  of 1.00 (mean error under 1%, five areas). Adding the material base colour makes the fit worse, so the viewer
  leaves it out.
- Water: Koopa Troopa Beach C's track is at Y 220; the sea surface around it is at 200.

Some areas over the sea have no water map of their own while an ancestor's covers them; the viewer falls back to the
nearest ancestor's water map there.

## How the viewer draws it

`TerrainRenderer` (App) keeps the quadtree. Each frame it walks from the root, frustum-culls areas, and refines an area
while one height cell would cover more than 3 pixels (3D: projected size at the distance to the area's box; top-down:
the zoom). An area is replaced by its children only once all visible children are loaded; quadrants with no child
keep drawing the parent (masked per quadrant). Tiles load on worker threads (3 parallel reads) and upload a few per
frame; at most 320 stay on the GPU, least recently drawn dropped first. Typical course views draw 15-65 tiles and
cache under 100, so only the area around the camera is ever read at high detail.

Shading: levels 0-4 use `albedo_far`; deeper levels blend the two materials of the 4 surrounding texels from the
albedo array (512² layers) and multiply by `color_mul2`. Normals come from the height map. Skirts hide cracks between
levels. Water is a translucent surface at the water height, hidden by the depth test where the ground is higher.
