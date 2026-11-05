# RSDB resource databases (`romfs/RSDB/*.Product.NNN.rstbl.byml.zs`)

zstd BYML. Each file is an array of rows (dictionaries); `__RowId` is the row's source path in the developers'
tree. `NNN` is the content version (180 = 1.8.0). Read with `mkwsat dump RSDB/<file>`.

| Table | Rows (1.8) | What it gives us |
|---|---|---|
| `Actor` | 4,826 | Every actor type: `Category` (StaticObj, PropObj, SystemObj, System, EffectObj, Driver, Vehicle, Item, ToolOnlyObj, …) and a Japanese `Label` written by the developers. Loaded by `Course/ActorDatabase.cs`; drives classification. |
| `Course` | 310 | One row per playable layout: `BancFile`, `GameRule`, `TrafficType`, `AudienceType`, `WorldConfigId`, `WorldTransformParentFile`/`Actor`, `DispName` (developer name, e.g. 「[映画館]SFCおばけぬま1」). See scene-layers.md. |
| `BancWorldTransformInfo` | 3,828 | Scene composition tree of the open world (child scene, parent scene, transform, depth). See scene-layers.md. |
| `CourseArea` | ~30 | Course areas with labels (e.g. `Cinema_00` 映画館). |
| `GameRule` | 5 | NormalRace, Survival, Balloon/Bomb/Coin battle. |
| `BancLink` | 4 | Link kinds: `BasicSignal`, `CreateLink`, `IslandExitSetLink`, `MessageLink`. |
| `BancParam` | 189 | Parameter schemas (name, type, default, Japanese label) for Banc `Dynamic` blocks, e.g. `Animal`: `IsFadeInOut`, `InitialWaitFrame`. Useful for naming `Dynamic` keys. |
| `WorldConfig` | 1 | `LowTide` water override (used by 2 course rows, e.g. `PeachBeachOriginal` variants). |
| `Scene` | 3 | `Boot`, `Game03` (the product game scene), `Game03ForConnection`. |
| others | | `Tag`, `TagDef`, `XLinkProperty*`, `StickerInfo`, `ComSetting` (CPU levels incl. ghost), `GraphRailDefine`, … (not used yet) |

## Actor parameters (`System/GymlArchive/*.sarc.zs`)
Not RSDB, but the next step from an `Actor` row: SARC archives of binary gyml (BYML). `actor_general_format` holds
`Gyml/ActorGParam/<Type>.gear__ActorGParam.bgyml` with `$parent`, `Label`, `BancDummyShape` (editor shape, e.g.
`Cube_c`, `Cone_z`) and `Components` (refs to `ObjectGParam` with the controller class `CtrlName`, `PhysicsGParam`,
`ExistConditionGParam`, `ModelInfoGParam`, …). `phive_general_format` holds the Phive rigid body / sensor params
(e.g. dash panels: a `Ground` body and a `<Type>_Zone` sensor on layer `VehicleArea`). An actor with no components
(e.g. `Area_DeleteLinkedActor_00`, `SystemObj_RespawnPoint_00`) has no behaviour of its own.

## Policy
Labels are game text: the repo only contains the English glosses we wrote for the handful of system types the
viewer names (`CourseObjectClassifier.SystemTypes`). The app reads the labels from the user's dump at runtime and
shows them in the inspector.
