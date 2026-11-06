# MKWSatellite: notes for Claude

C#/.NET 10 viewer/editor for Mario Kart World courses, aimed at speedrunners and time trialers. Projects:
`src/MKWSatellite.Formats` (parsers, no UI), `src/MKWSatellite.App` (Silk.NET GL + ImGui viewer),
`src/MKWSatellite.Cli` (`mkwsat` tools for checking formats against data), `tests/MKWSatellite.Formats.Tests`.

Start every session by reading `docs/ROADMAP.md` (plan + progress log), `docs/DECISIONS.md`, and the relevant
`docs/formats/*.md`.

## Build and run
- `dotnet build`, `dotnet test`.
- Game data comes from `--romfs <dir>`, `MKWSAT_ROMFS`, or the git-ignored `mkwsat.local.json`
  (see `mkwsat.local.example.json`). The reference dump is version 1.8 (`Product.180`).
- Headless renders: `MKWSatellite --course <name part> --screenshot out.png --size WxH [--3d --orbit yaw,pitch,dist | --first-person]
  [--no-ui] [--layer Field=value]`. Top-down shots larger than the window are tiled and stitched.
- `mkwsat` subcommands (`course`, `terrain`, `model`, `tex`, `dump`, `ground`, `areafit`, ...) check formats against
  the data; `MKWSAT_ALL=1` makes `course`/`model` list every type/shape.
- To let the user run the app while you keep building, copy the App bin folder elsewhere and run the copy.
- Release packages: `pwsh dist/publish.ps1` (viewer only: self-contained folders per OS + a portable framework-dependent one; see dist/README.md).

## Rules (from the user)
- Never commit absolute paths from the user's machine, game data, keys (`*.keys`), or screenshots.
- The project is GPL-3.0 (LICENSE, D19). Respect licences: check a project's licence before borrowing from it and credit it (THIRD_PARTY_NOTICES.md).
- Commit and push small steps to `main`; update `docs/` in the same commit (progress log, decisions, format notes).
- Don't "fix" Nintendo's data oddities; only fix our own interpretation errors.
- Mark anything not confirmed in game code or data as **unverified** in docs and UI text.
- The dump is on a slow HDD: list only specific folders, never recurse romfs. `TerrainArc/Game03_Proc/` has 43k
  files and `Model/` ~5.8k: open files by exact name rather than listing.
- Write large multi-line edit scripts to scratch files, not nested shell heredocs.

## IDA
An IDA database of the 1.8 executable is available through the IDA MCP tools. Use it freely to verify how the game
reads a field or format instead of guessing from data alone. Renaming functions/globals as you learn them is
welcome when you are quite confident; record traced addresses and function names in the docs. It is a side
benefit, not a requirement.
- Hashes: controller/`Dynamic` keys are MurmurHash3_32 (seed 0) of the name; gyml `$type` is MurmurHash64A (seed
  0xA9F63E6017234875). Names missing as strings can be found by hashing and scanning MOVZ/MOVK constants.

## Where things are (details in docs/formats)
- Courses: `Banc/Race_*.bcett.byml.zs` + world tiles `Banc/World_Game03/XXZZ` or `<Course>_00_Fix` (scene-layers.md).
- Models/textures: BFRES 10 + BNTX 4.1, Switch 2 block-linear layout (models.md).
- Terrain: `TerrainArc/Game03_Proc.tscb` + per-area TARB maps (terrain.md).
- Collision: `Phive/StaticCompoundBody/{Course,World_Game03}/*.bphsc`, `Phive/Terrain/Game03_Proc/*.bphsh`, `_Fix` stage shapes in `Phive/Shape/Dcc/` (collision.md; check with `mkwsat collision <course>`).
- Merged world geometry (city streets, track parts, street furniture): `Model/Piece_Gridify_Game03_X##_Z##.*`
  (terrain.md, "World pieces").
