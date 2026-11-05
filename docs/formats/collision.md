# Collision (Phive / Havok)

Course collision as read by `MKWSatellite.Formats.Phive` and shown by the viewer's "Collision" layer. Everything
here comes from the 1.8 dump (`Product.180`). Layouts were read from the files' own Havok reflection data, and
results were checked against the course data (see "Checks"). They were not traced in the game code unless noted.

## Where a course's collision comes from

| Source | Path | Notes |
|---|---|---|
| Race scene compound | `Phive/StaticCompoundBody/Course/<race scene>.Nin_Ounce_NVN2.bphsc.zs` | Race-only objects plus respawn (`Rescue`), `Valley` and no-jump-action (`NoJA`) volumes. `RaceStage_*` holds the same in local coordinates. |
| World compound cells | `Phive/StaticCompoundBody/World_Game03/World_Game03_XXZZ_ij.Nin_Ounce_NVN2.bphsc.zs` | Open-world courses. `XX`/`ZZ` = 1000-unit tile, `i` = X and `j` = Z 250-unit cell within it (checked against edge continuity). Holds the stage model's collision (96% of a stage's vertices within 5 cm once the actor rotation is applied), the merged world geometry and fixed objects. 4096 files; open by name. |
| Terrain | `Phive/Terrain/Game03_Proc/X##_Z##_ij.Nin_Ounce_NVN2.bphsh.zs` | Mesh local to the cell centre (±125 in X/Z), absolute heights. Cell centre = `((tile·4 + i − 32 + 0.5)·250, (tile·4 + j − 32 + 0.5)·250)`. Materials `Dirt`/`Road` + `Kusa`/`Soil`/`Snow`..., tagged `Terrain`. |
| Stage shapes (off-world `_Fix` courses) | `Phive/Shape/Dcc/<name>.Nin_Ounce_NVN2.bphsh.zs` | Per `Stage_*` actor: ActorGParam `Components.PhysicsRef` → PhysicsGParam `ControllerSetResourcePath` → ControllerSetParam `ShapeNamePathAry[].FilePath` → ShapeParam `PhshMesh[].PhshMeshPath` (`Work/Phive/Shape/Dcc/X.phsh`). Placed with the actor's scale, Euler XYZ rotation and position. |

Other folders, not used yet: `World_Game03_Free`, `_Foliage`, `_Water`, `_ModelWater` (4096 each), `Stage_Rainbow`,
`Stage_Lava`, `Stage_KoopaTroopaBeach` (water only), `Mission`.

## Phive container

Little-endian. Magic `Phive\0`, byte 6 = 1, BOM `FF FE` at 8, then u32 offsets from 0xC.

**`.bphsh` (shape):** `+0xC` tagfile offset (0x30), `+0x10` material table, `+0x14` filter table, `+0x18` end,
`+0x1C` tagfile size, `+0x20` material table size, `+0x24` filter size. The tagfile's first item is the root shape.

**`.bphsc` (static compound):** version bytes `02 02`, 17 offsets at 0xC:
- `offs[4]..offs[5]`: one 20-byte record per top-level compound instance: `u32 index, u32 layer, u16, u16,
  u32 firstMaterial, u32 materialCount`.
- `offs[5]..offs[6]`: material table. `offs[6]`: filter table.
- `offs[11..14]`: up to three tagfiles, each with one `hknpCompoundShape`. Sections 8–10 hold other data (one world
  cell has a 0x20000-byte grid there), so don't search for `TAG0`.
- Coordinates are world space.

**Material entry (16 bytes):** `u32 Material, u32 SubMaterial, u64 UserShapeTag bits`. Names come from
`Phive/Config/PhiveConfig.byml`:
- `MaterialCollection` (gameplay, 38): Road, Dirt levels, Slip, Wall, AirWall, Dash, Glide, Valley, Rescue, Lava, ...
- `SubMaterialCollection` (surface, 34): Asphalt, Snow, Kusa (grass), Kanaami (wire mesh), Rainbow, ...
- `UserShapeTagCollection` bits: Terrain, Road, Fence, Unridable, DisableSurfaceVelocity, Attach, DecalTrail,
  WallRideOff, Step, NoJA, LightOn, LightOff, SideReject.

**Record layer** values seen: `0x400` (bits 8–15 = 4 = `Ground` in `LayerEntityCollection`), `0x1600` (22 =
`Valley`), `0x701` and `0x180400`. A low byte of 1 is read as a sensor layer (`0x701` → `VehicleArea`; it holds the
Dash/Rescue/NoJA volumes). **Unverified**; the meaning of `0x180400`'s upper bits is unknown.

## Havok tagfile (TAG0, SDK `20230200`)

Chunks: u32 big-endian header (top 2 bits flags, low 30 bits size including the 8-byte header), then a 4-char tag.
`TAG0` holds `SDKV`, `DATA`, `TYPE {TPTR, TST1, TNA1, FST1, TBDY, THSH, TPAD}`, `INDX {ITEM}`.

- Varint: the first byte's top bits give the length: `0xxxxxxx` 1 byte, `10` 2, `110` 3, `11100` 4 (`11101` 5,
  `11110` 6 are not used here).
- `TST1`/`FST1`: NUL-separated type and field names.
- `TNA1`: count, then per type (from 1): name index, parameter count, (name index, value) pairs. Parameters whose
  name starts with `t` are type indices.
- `TBDY`: type index (0 = skip), parent, option bits, then by option: 1 format, 2 subtype, 4 version, 8 size +
  alignment, 0x10 flags, 0x20 members `(name, flags, [one extra varint if flags & 0x80], offset, type)`, 0x40
  interfaces (pairs), 0x80 one varint.
- `ITEM`: 12 bytes, `u32 type | flags << 24, u32 offset into DATA, u32 count`. A shape's class is found by matching
  its address to an item.
- Data is in place with relative pointers: `hkRelArrayView` = (i32 offset from the field, i32 count),
  `hkRelArray` = (i64 offset, i32 size, i32 capacity), `hkRelPtr` = i64.

## Shapes

Offsets below are the reflected ones; the reader asks the reflection first and uses these as fallbacks.

**`hknpMeshShape`** (size 160): `+0x50` vertexConversionUtil (`bitScale16` vec4, `bitScale16Inv` vec4 at `+0x60`,
1/512 seen), `+0x70` shapeTagTable view of `(u32 key, u16 tag)` 8-byte entries, `+0x90` geometrySections view of
64-byte sections.
- Section: `+0x08` primitives view (`u8 a, b, c, d`), `+0x10` vertex buffer view (bytes), `+0x20` `i32 x3`
  sectionOffset.
- Vertex = `(u16 x3 + sectionOffset) × bitScale16Inv`. If `sectionOffset.x == 0x7FFFFFFF` the section stores plain
  float3 vertices (12 bytes).
- Primitive = quad: triangles `(a, b, c)` and, if `c != d`, `(a, c, d)`.
- Triangle tags: table sorted by key `section << 9 | primitive << 1`. A tag applies from its key until the next
  entry (run-length). `0xFFFF` = none. In a `.bphsh` the tag indexes the material table. In a compound it counts
  from the instance's record `firstMaterial`.

**`hknpCompoundShape`** (size 192): `+0x48` instances (`hkFreeListRelArray`: i64 offset, i32 count) of 64 bytes:
quaternion xyzw `+0`, translation `+0x10`, scale `+0x20`, `u16 shapeTag +0x2C`, child `hkRelPtr +0x30`. A top-level
instance's `shapeTag` picks its record. Children of nested compounds carry `0xFFFF`; they take the record's material
at their child index. This holds for every nested compound seen (count = children), but is **unverified**.

**Convex shapes** (`hknpBoxShape`, `hknpConvexShape`, `hknpCapsuleShape`, `hknpConvexPolytopeShape`,
`hknpSphereShape`): convexRadius `+0x20`, hull `+0x40` with views: vertices (float3) `+0`, planes `+8`, faces
`+0x10` (`u16 first, u8 count, u8`), indices `u8 +0x18`. A capsule is two hull vertices + radius; drawn as an
8-sided tube.

## Checks

`mkwsat collision <course>` lists sources and per-material triangle counts. It also drops a vertical ray from every
lap node and reports the node's height above the first surface and that surface's material.

| Course | Files | Triangles | Lap nodes on collision | Node height above surface (median) | Surface under nodes |
|---|---|---|---|---|---|
| DK Summit (`Race_DKPass_A`) | 41 | 457,848 | 51/51 | 0.01 | Slip/Snow 26, Road/Asphalt 20 |
| Moo Moo Meadows | 33 | 197,436 | 36/36 | 0.88 | Road/Soil [Terrain] 30, Dirt/Kusa 5 |
| Crown City (`Race_WestCity_A`) | 41 | 350,960 | 52/52 | 0.00 | Road/Asphalt 35, Road/Concrete 10 |
| Rainbow Road | 5 | 183,750 | 203/219 | 0.00 | Road/Rainbow 142 |

Reading a course takes 0.1–0.5 s. DK Summit's snowy road is `Slip`/`Snow`, which matches how it drives.

## Viewer

"Collision (Phive)" in the left panel (off by default) loads the course's collision on a worker thread.
- Colour by gameplay material or by surface.
- Opacity slider and triangle edges.
- Volumes (sensor layers, the Valley layer, AirWall/Valley/Rescue/Fall materials) are drawn see-through and can be
  hidden.
- Volumes keep their gameplay colour in surface mode. "Highlight" (default `Rescue`) draws one volume material
  stronger, with light edges and faintly through walls; "Volume opacity" sets the others. `Rescue` is also used on
  solid-looking meshes (e.g. Rainbow Road's clouds and the space station's undersides), not only on boxes.
- A per-material list with counts; unticking a material hides it.
- Hovering shows the material, surface, tags, layer and position under the cursor.
- Screenshots: `--layer Collision=1 [--layer CollisionBySurface=1] [--layer CollisionHighlight=Rescue]`.

## Vehicle hitboxes (bonus, **unverified**)

Vehicles have no Phive shape: their PhysicsGParam (`VehicleBase`) is just `IsVehicle: true` with an empty
`ControllerSetResourcePath`. The data that looks like a hitbox is in `System/GymlArchive/general_format` →
`Gyml/CustomizerVehicleInfoGParam/<Vehicle>.game__CustomizerVehicleInfoGParam`, `ExtraParam`:
- `LocalCollisionBox`: `BodySize`, `CabinOffset` + `CabinSize`, `FloatOffset` + `FloatSize` (vec3s). `Common` gives
  body 1 × 0.1 × 1, cabin 0.5³ at +0.5 Y. 14 of 71 entries override it (e.g. `Vehicle_Rcc_00` body 1.12 × 0.3 × 1.99).
- `AxleData[].TireData`: `Radius`, `Width` (+ dirt scales) per axle.

`mkwsat vehicles` lists these. Not known yet:
- whether sizes are half or full extents;
- the units and the per-vehicle scale;
- how the code builds the rigid body. The executable has `game__VehicleRigidBodyShapeGParam` and
  `game__VehicleLocalRigidBodyGParam` types with no data files, so their values may be code defaults.

Character (driver) Phive params are cloth/helper bones (`HelperBoneParam`), not hitboxes. Not drawn in the viewer.

Not done: dynamic actors' own collision (only what is baked into the compounds), `World_Game03_Water`/`_Foliage`, and
kart/character hitboxes.
