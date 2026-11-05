# MKWSatellite

A course viewer (and eventually a basic editor) for **Mario Kart World** (Nintendo Switch 2), aimed at showing
course data accurately for speedrunners and time trialers: lap checkpoints, respawn zones, boost placement,
item boxes, AI routes and, later, collision surfaces.

> Early skeleton. Nothing here is affiliated with or endorsed by Nintendo. You need your own legally dumped copy
> of the game; no game data is included or should ever be committed.

## Layout

| Project | Purpose |
|---|---|
| `src/MKWSatellite.Formats` | Game file formats with no UI/GPU dependencies: zstd, BYML, romfs layering, Banc course scenes. Later: BFRES models, BNTX textures, Phive collision. |
| `src/MKWSatellite.App` | Viewer: Silk.NET (OpenGL) + Dear ImGui. Top-down 2D course view with layers, inspector and ruler. |
| `src/MKWSatellite.Cli` | `mkwsat` command-line tools (`info`, `banc`, `scan`, `dump`, `course`). |
| `tests/MKWSatellite.Formats.Tests` | Unit tests that run without game data. |

## Requirements

- .NET 10 SDK
- An extracted romfs of the game (base, plus optionally an update romfs layered on top)

## Pointing it at your dump

Paths are machine-specific and must never be committed. Use one of, in priority order:

1. Command line: `mkwsat --romfs <base romfs> [--romfs <update romfs>] info`, or `MKWSatellite <base romfs> [<update romfs>]`
2. Environment variable `MKWSAT_ROMFS` (paths separated by `;` on Windows, `:` elsewhere; base first)
3. `mkwsat.local.json` in the working directory or next to the executable (git-ignored). Copy
   `mkwsat.local.example.json` and fill it in.

Later romfs layers override earlier ones, which is how game updates work (an update romfs only contains changed files).

## Build and run

```sh
dotnet build
dotnet test
dotnet run --project src/MKWSatellite.Cli -- info
dotnet run --project src/MKWSatellite.Cli -- banc "Race_Cinema_A_00[Game03].bcett.byml.zs"
dotnet run --project src/MKWSatellite.Cli -- scan --filter Race_          # parse every scene, census of keys
dotnet run --project src/MKWSatellite.Cli -- course Cinema_A               # assembled course summary
dotnet run --project src/MKWSatellite.Cli -- dump Banc/PathAdditionalData/Race_Cinema_A_00.byml
dotnet run --project src/MKWSatellite.App
dotnet run --project src/MKWSatellite.App -- --course Rainbow --screenshot rainbow.png --size 1800x1050
```

Viewer controls: left-drag pans, mouse wheel zooms, click selects (inspector on the right), right-drag measures.

## Docs

Progress, decisions and format notes live in [docs/](docs/README.md).

## Licensing

The project licence has not been chosen yet. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for dependencies
and the rules for reusing outside code (notably: Switch Toolbox is GPL-3.0 and must not be copied in unless the
project adopts a compatible licence).
