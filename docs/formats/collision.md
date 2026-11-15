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

**Volumes vs area actors.** `mkwsat collision` also splits the `Rescue`/`Valley`/`Fall` triangles into connected
pieces and matches each `SystemObj_RescueArea_00`/`SystemObj_ValleyArea_00` actor's placed physics box to the piece it
overlaps most:

| Course | Area actors | Matched (IoU) | Pieces with no actor |
|---|---|---|---|
| Airship Fortress | 2 | 2 (0.86) | 0 |
| DK Summit | 13 | 13 (0.86) | 0 |
| Crown City | 9 | 9 (0.86) | 1 Rescue sensor box, 2 Valley boxes, 7 `Rescue` surfaces on the Ground layer (Kusa/Concrete/Iron) |
| Rainbow Road | 10 | 10 (0.86) | 488 `Rescue` surfaces on stage meshes (Cloud, Iron, Kusa, Ice, Concrete) |

So the sensor `Rescue` and `Valley`-layer boxes in the race compound are the area actors' own shapes baked in, not a
separate kind of zone. The collision box is slightly smaller on every side than the actor box (0.5–1.8 units, not
uniform per axis; maybe convex-radius handling, **unverified**). The `Rescue` material on ordinary Ground-layer
geometry has no area actor; reading it as "touching this surface respawns you" is **unverified**. A few Crown City
boxes have no actor in the race scene; their source is unknown.

Reading a course takes 0.1–0.5 s. DK Summit's snowy road is `Slip`/`Snow`, which matches how it drives.

## Performance surfaces (Tarmac / Gravel / Water)

Kart stats have per-terrain columns in `System/CombinationDataTableData/PerformanceParamTableData` (rows `Level00`
to `Level25`, column enum below). Traced in the 1.8 executable: `sub_710065B660`, block 0x710065BAD8–0x710065BB4C,
writes a (top speed, handling) stat pair to the kart's performance object (`kart+0x178`, fields `+0x90`/`+0x94`).
Checks in order (`st` = ground state at `kart+0x160`):

| # | Condition | Top speed / handling column |
|---|---|---|
| 1 | `st+0x5C` bit 14 (rail grind) | `TopSpeedRailSlide`; handling unchanged |
| 2 | `st+0x5C` bit 17 (wall ride) | `TopSpeedWallRide`; handling unchanged |
| 3 | `kart+0x190` bit 9 or `st+0x5C` bit 25 (water) | `TopSpeedWater` / `HandlingWater` |
| 4 | dirt rate `st+0x298` > 0 | `TopSpeedDirt` / `HandlingDirt` |
| 5 | contact SubMaterial in mask `0x27C05E0F0` | `TopSpeedTarmac` / `HandlingTarmac` |
| 6 | anything else | `TopSpeedGravel` / `HandlingGravel` |

- **Tarmac SubMaterials:** Asphalt, Concrete, Stone, Wood, Iron, Kanaami, Glass, Carpet, Rainbow, Vinyl, Plastic,
  Biscuit, Candy, ChocoChip, Bone. **Every other SubMaterial is Gravel**: Kusa, Soil, Sand, Snow, Ice, Cloud, Mud,
  Leaf, petals/flowers, Undefined... So Gravel is chosen by the surface, not by the gameplay material: a `Road`
  triangle with `Soil` (Moo Moo Meadows' track) or `Snow` (DK Summit) uses the Gravel stats.
- **Dirt:** the contact builder `sub_71002008B0` (from 0x71002016AC) sets ground bits 9/10/11 for `LowDirt`/`Dirt`/
  `HighDirt` and then the dirt rate to 1.0 (0x710020665C). So all three Dirt materials use the Dirt columns, which hold
  no values in 1.8. What that does to speed is **unverified** (the `VehicleGParam` `DirtSpeedParam` rates may apply).
- **Water** comes from the kart's depth in a water surface (0x7100204E10–0x7100204E70: depth below −0.1 sets the
  water bit), not from a triangle. The viewer shows the `Water` material as the Water group as a stand-in
  (**unverified**). `Shallows` and `MuddyWater` go by their SubMaterial here.
- No other gameplay material is tested: `Slip`, `Dash`, `Gravity`, `Bumpy`, `Glide`... all go by SubMaterial. Glide
  speed (`TopSpeedGlide`) and `Acceleration` are read separately. Nothing in the block checks for being airborne, so
  the last contact presumably still counts in the air (**unverified**).
- The contact record offsets `+0xD8` (Material) and `+0xDC` (SubMaterial) are inferred from the values compared
  (bound 33 = `Bone`; `+0xD8` is compared with `Trampoline`/`MuddyWater` elsewhere).
- Stat enum (string table 0x7103592098): 0 None, 1 TopSpeedTarmac, 2 TopSpeedGravel, 3 TopSpeedDirt, 4 TopSpeedWater,
  5 TopSpeedGlide, 6 TopSpeedRailSlide, 7 TopSpeedWallRide, 8 Acceleration, 9 Weight, 10 HandlingTarmac,
  11 HandlingGravel, 12 HandlingDirt, 13 HandlingWater, 14 HandlingGlide, 15 HandlingGrip, 16 TireGrip,
  17 CoinSpeedRatio, 18 DriftDashTime, 19 InvincibleRatio. The table is cached as [27][20] floats at 0x71037B8C4C
  (`sub_7100993200`); row 26 replaces the level when `kart[0x10]+0x110` bit 3 is set (all stats but Weight).

`PerformanceSurfaces.Of` (Formats) implements rows 3–6 per triangle, with walls, volumes, poles and pickups as
"none".

## Object collision

Placed objects can have their own shapes: ActorGParam `Components.PhysicsRef` → PhysicsGParam
`ControllerSetResourcePath` → ControllerSetParam `ShapeNamePathAry[].FilePath` → ShapeParam `PhshMesh[].PhshMeshPath`
(the same chain as `_Fix` stage shapes), placed with the object's scale, Euler XYZ rotation and position.
`mkwsat collision <course> --objects` lists them and how many of each one's placed vertices already lie on the loaded
compounds (within 0.25 units):

| Course | Types | Already in the compounds (100%) | Not in them (0%) |
|---|---|---|---|
| Crown City | 18 | flag ropes, arrow signs, audience stands, glide boards, `SystemObj_CollisionDogleg_00` air walls | traffic (`CmnObj_Mobcar*_MoveN_00`, `Mobship*_Move_00`), vending machines |
| DK Summit | 7 | audience stands, arrow signs, flag ropes, glide ramp | `CmnObj_MobcarCarrier000_MoveN_00` |
| Rainbow Road | 29 | one glide board | lap and goal gates, arrow signs; dash and glide plates 0–78% (only where they touch the road) |

So static objects are baked into the static compounds, and moving or spawned ones are not. The viewer adds every
instance with less than 90% of its vertices already present ("Object collision", on by default), at the position the
scene gives. Moving objects (traffic, boats) start there and then follow their paths, so their drawn collision is a
snapshot. Objects whose shape is a primitive (`Box`, `Sphere`... in ShapeParam, no `PhshMesh`) are not drawn yet.

## Viewer

"Collision (Phive)" in the left panel (off by default) loads the course's collision on a worker thread.
- Colour by gameplay material, by surface, or by **performance** group (Tarmac / Gravel / Water / Dirt, above).
  "Only performance surfaces" hides everything else (walls, volumes...).
- "Object collision": placed objects' own shapes that are not in the compounds (see above), on by default.
- Opacity slider and triangle edges.
- Volumes (sensor layers, the Valley layer, AirWall/Valley/Rescue/Fall materials) are drawn see-through and can be
  hidden.
- Volumes keep their gameplay colour in surface mode. "Highlight" (default `Rescue`) draws one volume material
  stronger, with light edges and faintly through walls; "Volume opacity" sets the others. `Rescue` is also used on
  solid-looking meshes (e.g. Rainbow Road's clouds and the space station's undersides), not only on boxes.
- A per-material list with counts; unticking a material hides it.
- Hovering shows the material, surface, tags, layer, performance group, object (if any) and position under the cursor.
- Screenshots: `--layer Collision=1 [--layer CollisionBySurface=1 | --layer CollisionByPerformance=1
  [--layer CollisionPerformanceOnly=1]] [--layer CollisionObjects=0] [--layer CollisionHighlight=Rescue]`.

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

Not done: objects with primitive-only shapes, moving objects' paths, `World_Game03_Water`/`_Foliage`, and
kart/character hitboxes.
