# Models and textures (BFRES 10, BNTX 4.1, gyml model links)

All readers are our own managed C# (no native libraries, no Switch Toolbox code), written from the files plus
public format knowledge. Code: `Formats/Models`, `Formats/Textures`, `Formats/Archives/Sarc.cs`.

## Which model an actor uses (verified in 1.8 data)
1. `System/GymlArchive/actor_general_format.Product_NNN.sarc.zs` → `Gyml/ActorGParam/<Type>.gear__ActorGParam.bgyml`
   (BYML) → `Components.ModelInfoRef`.
2. `modelinfo_general_format` → `Gyml/Component/ModelInfoGParam/<X>.gear__ModelInfoGParam.bgyml` → `ModelResource[]`.
   The near model is the first entry without `ResourceType` (`FarModel0`/`FarModel1` are LODs; stages also list a
   `_Cubemap` model).
3. `Fmdb` `Work/Model/<Group>/<Folder>/output/<Model>.fmdb` → romfs `Model/<Folder>.<Model>.bfres.zs`.
4. Any gyml can inherit missing keys via `$parent` (followed up to 16 levels).

Course geometry is ordinary actors named `Stage_<Course>_<Variant>_00`, placed in `World_Game03/WorldResident`
(e.g. `Stage_Cinema_A_00` at (2255, 542, -4952)), so the course loader's world layer already picks them up.
Open-world ground is **not** in these models: it is the terrain system (`TerrainArc`), see terrain.md. BNTX texture
arrays: each layer is a full mip chain, layers are `image size / array length` bytes apart (used for the terrain atlas).

## BFRES (file version 10.x, `FRES    `)
Matches the Switch v9 layout for everything a viewer needs:
- **Header:** +0x28 model array, +0xB0 buffer info `{u32, u32 size, u64 offset}` (all vertex/index data is
  relative to that offset), +0xDC model count.
- **FMDL (0x78):** +0x08 name, +0x18 skeleton, +0x20 vertex buffers, +0x28 shapes, +0x38 materials,
  +0x68 counts (vertex buffers, shapes, materials).
- **FSKL:** +0x10 bones (0x58 each: name, +0x22 parent, +0x2C flags (0x1000 = Euler), +0x30 scale, +0x3C rotation,
  +0x4C translation), +0x18 matrix-to-bone list, +0x38/3A/3C bone/smooth/rigid counts.
- **FVTX (0x58):** +0x08 attributes (0x10 each: name `_p0/_n0/_t0/_u0/_u1/_c0/_i0/_w0`, format u32 =
  (layout << 8) | type, offset u16, buffer u16), +0x30 size array, +0x38 stride array (0x10-byte entries),
  +0x48 data offset, +0x4C attribute count, +0x4D buffer count, +0x50 vertex count. Typical: position f32×3,
  normal/tangent 10-10-10-2 snorm, UVs f16×2 or u16×2 unorm.
- **FSHP (0x60):** +0x18 meshes (one per LOD), +0x52 material, +0x54 bone, +0x56 vertex buffer, +0x5A skin count,
  +0x5B mesh count.
- **Mesh (0x38):** +0x20 index offset, +0x24 primitive (3 = triangles), +0x28 index format (1 = u16, 2 = u32),
  +0x2C index count, +0x30 first vertex, +0x34 sub-mesh count.
- **FMAT (0xB0):** changed from v9. Only these are used: +0x08 name, +0x20 texture name array, +0xA3 texture count.
  Textures are referenced by name and stored separately as `Tex/<name>.bntx.zs`; the albedo is the `*_Alb` one.

## BNTX (version 4.1.1)
- Platform block tag is `Ounc` (Switch 1: `NX  `); +0x24 texture count, +0x28 pointer array to `BRTI` blocks.
- **BRTI:** +0x12 tile mode (0 = block linear), +0x16 mips, +0x1C format, +0x24 width/height/depth/array,
  +0x34 block height log2, +0x50 image size, +0x60 name, +0x70 mip offset table (absolute file offsets).
- **Formats seen** (1/40 sample of `Tex/`): albedo is almost all ASTC sRGB (8×8, 6×6, 5×5). Also BC1 sRGB (bake maps),
  BC4/BC5 (masks, normals), BC6H (baked light), BC7 (sky), R16/R16G16 (terrain/grass height maps).
  BC6H and BC7 are not decoded yet; nothing the viewer draws needs them.

### Block-linear layout differs from Switch 1 (found empirically)
The GOB is still 64 bytes × 8 rows with blocks of 2^n GOBs stacked vertically, but **inside the GOB the order is
different**: address bits from low to high are x0..x3, y0, y1, x4, y2, x5 (16-byte × 4-row sectors). Switch 1's is
x0..x3, y0, x4, y1, y2, x5. Found by trying every ordering of the six GOB address bits and keeping the one that gives
the smoothest image (a BC1 texture gave a clear winner), then confirmed by decoding ASTC and BC1 textures at several
mip levels. Smaller mips shrink the block height while it exceeds half the mip's height in GOBs (standard rule;
visually confirmed).

## Viewer
`App/Rendering/ModelLibrary.cs` loads on worker threads, uploads a few models per frame and shares models and
textures between instances. Textures are capped at 1024 px by picking a smaller stored mip. Shading is albedo with one
directional light and alpha cutout (`Cutout` in material/texture name), and is not the game's look. Instance
transform = Scale × EulerXYZ(Rotate) × Translate, the same convention as the course objects.
