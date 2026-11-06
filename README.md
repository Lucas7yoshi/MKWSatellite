# MKWSatellite

> Hello, I'm Lucas7yoshi and this bit is actually written by me. The rest of this is written by claude code - it has
> been tested by me as in... using it, but as for the code itself, the quality of it, it is not. Use this at your own
> risk.
>
> This is primarily just a stepping stone for modding, the tool *kinda* does it, but its completely untested because
> my Switch 2 is not in a moddable state. The real true goal of this for now is just for analysis of track bounds and
> OOB and so on, which is where the main focus is, not on accurate rendering or terrain editing or really... anything
> more than that.
>
> The remainder of this README is written by Claude, I am releasing this repo as GPL 3 in line with switch toolbox
> which was used as reference for formats by Claude, and you are more than welcome to pilfer this, and use the
> information its documented in docs/ from IDA and so on to build something more polished, with or without "credit" (which
> is arguably a bit silly given im just the guinea pig testing it for the most part and giving it IDA).

## Features

A course viewer for **Mario Kart World** (Nintendo Switch 2, data version 1.8.0), built to show where a course's
bounds really are.

**Lap and bounds data**
- Lap path with every lap gate (checkpoint gates numbered), drawn from the game's own gate maths.
- Lap progress zones: the volume the lap tracker follows. Leaving all of them triggers a Lakitu rescue
  ([how that works](docs/formats/out-of-bounds.md)).
- Rescue (Lakitu) and valley areas at their real collision-box size, recovery (respawn) points, high-speed, rule
  (no reverse, no jump...), invisible dash/glide and link areas.
- Bullet Bill zones, including where a Bullet Bill can't end.
- Collision from the game's Phive data, coloured by surface material or by type, with the rescue/valley volumes and
  surfaces highlighted.

**Everything else on the course**
- Item boxes, coins, dash panels, glide ramps, boost rings, start grid, hazards, fences, audience and scenery.
- AI routes (default, alternative and Bullet Bill only), grind rails, object and glide paths.
- Course models (BFRES/BNTX), open-world terrain and water, and the merged world geometry around the course.

**Viewer**
- Top-down, 3D orbit and first-person cameras.
- Click anything for its details in the inspector; distance ruler in top-down.
- Layer toggles for every category, and per scene layer (race scene, world, course `_Fix` layer).
- Headless screenshots, including top-down renders larger than the screen (e.g. 4K maps of every course).
- Set up the game folder from the viewer itself (**Game data**).

**Editing (experimental, untested in game)**
- Move objects and path points in the race scene, with undo/redo, and save the scene to a separate mod folder.
  Nothing has been checked on a real console.

**Research notes**
- [docs/](docs/README.md) records what was learned about the formats and game logic, much of it traced in the game
  code (IDA): course scenes, rails, collision, terrain, models, and the Lakitu/out-of-bounds rules. Anything not
  confirmed is marked **unverified**.

> Not affiliated with or endorsed by Nintendo. You need your own dump of the game; no game data is included, and none
> should ever be committed.

## Getting started

1. Download a release package. Take the one for your system (`win-x64`, `linux-x64`, `osx-arm64`: nothing else to
   install), or the smaller `portable` one, which runs on any OS but needs the
   [.NET 10 runtime](https://dotnet.microsoft.com/download/dotnet/10.0).
2. Unpack it and run `MKWSatellite` (portable: `MKWSatellite.exe`, or `dotnet MKWSatellite.dll`).
3. Open **Game data** at the top of the left panel, enter your extracted romfs folder (add the update's romfs after
   the base game's if they are separate) and press **Save and load**.
4. Pick a course from the list.

Linux and macOS packages are built but **untested**.

### Controls

| View | Controls |
|---|---|
| Top-down | Left-drag pan, wheel zoom, click select, right-drag measure |
| 3D | Left-drag orbit, right-drag pan, wheel zoom, WASD/QE move (Shift faster) |
| First person | Right-drag look, WASD move, E/Q up/down, Shift faster, wheel sets speed |
| Editing | Ctrl+drag moves the selection, Ctrl+Z / Ctrl+Y undo/redo |

### Where the game folder setting is kept

**Game data** writes `mkwsat.local.json` next to the executable (or updates one in the working directory). The viewer
and tools look in this order:

1. Command line: `MKWSatellite <base romfs> [<update romfs>]`, or `mkwsat --romfs <base> [--romfs <update>] ...`
2. Environment variable `MKWSAT_ROMFS` (paths separated by `;` on Windows, `:` elsewhere; base first)
3. `mkwsat.local.json` in the working directory or next to the executable (see `mkwsat.local.example.json`)

Later romfs folders override earlier ones, which is how game updates work.

## Building from source

Needs the .NET 10 SDK.

```sh
dotnet build
dotnet test
dotnet run --project src/MKWSatellite.App
dotnet run --project src/MKWSatellite.App -- --course Rainbow --screenshot rainbow.png --size 3840x2160 --no-ui
dotnet run --project src/MKWSatellite.Cli -- info
dotnet run --project src/MKWSatellite.Cli -- course DesertHills_A    # assembled course summary
dotnet run --project src/MKWSatellite.Cli -- collision DesertHills_A # collision sources and materials
dotnet run --project src/MKWSatellite.Cli -- scan --filter Race_     # parse every scene
```

Viewer options: `--course <name part>`, `--3d`, `--first-person`, `--orbit yaw,pitch[,distance]`, `--no-models`,
`--no-ui`, `--size WxH`, `--screenshot out.png`, `--layer Field=value` (any `ViewLayers` field, e.g. `AiRoutes=0`).
Release packages: [dist/README.md](dist/README.md).

| Project | Purpose |
|---|---|
| `src/MKWSatellite.Formats` | Game formats, no UI: zstd, BYML (read/write), layered romfs, Banc scenes, BFRES 10, BNTX 4.1 (ASTC/BC decoding), Havok/Phive collision, terrain |
| `src/MKWSatellite.App` | The viewer: Silk.NET (OpenGL) + Dear ImGui |
| `src/MKWSatellite.Cli` | `mkwsat`: command-line checks of the formats against the game data |
| `tests/MKWSatellite.Formats.Tests` | Unit tests that run without game data |

## Licence

MKWSatellite is licensed under the [GNU General Public License v3.0](LICENSE). Switch Toolbox (also GPL-3.0) was used
as a format reference. Dependencies and their licences: [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
