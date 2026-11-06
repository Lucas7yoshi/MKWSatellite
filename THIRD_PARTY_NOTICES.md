# Third-party notices

MKWSatellite uses the following packages from NuGet. They are referenced, not vendored; their licences apply to their own code. Release packages (`dist/`) redistribute the App's package binaries, including the native GLFW and cimgui libraries, together with this file.

| Package | Used by | Licence |
|---|---|---|
| [ZstdSharp.Port](https://github.com/oleg-st/ZstdSharp) | Formats | MIT |
| [Silk.NET](https://github.com/dotnet/Silk.NET) (Windowing, Input, OpenGL, OpenGL.Extensions.ImGui) | App | MIT |
| [ImGui.NET](https://github.com/ImGuiNET/ImGui.NET) (via Silk.NET ImGui extension) | App | MIT |
| [Dear ImGui](https://github.com/ocornut/imgui) (native, via ImGui.NET) | App | MIT |
| [GLFW](https://www.glfw.org/) (native, via Silk.NET / Ultz.Native.GLFW) | App | zlib/libpng |
| Microsoft.Extensions.DependencyModel, Microsoft.DotNet.PlatformAbstractions (via Silk.NET) | App | MIT |
| [System.CommandLine](https://github.com/dotnet/command-line-api) | Cli | MIT |
| [xUnit](https://github.com/xunit/xunit), Microsoft.NET.Test.Sdk, coverlet | Tests only | Apache-2.0 / MIT |

## Rules for adding code from elsewhere

- **Switch Toolbox** (KillzXGaming/Switch-Toolbox and its Switch 2 forks) is **GPL-3.0**. Do not copy its code into this repository unless the whole project is licensed GPL-3.0-compatible. Reading it as a format reference and writing our own implementation is fine.
- The original **Syroot NintenTools.Bfres** library is **MIT**. Forks may carry different licences: check the fork's own LICENSE before reusing any code, and list it here.
- Format documentation from community wikis (e.g. the Zelda modding EPD wiki for Phive/Havok) may be used as a reference; credit it in the relevant source file.
- No game data (romfs/exefs files, extracted models, textures, audio) may be committed.
