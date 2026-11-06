# Release builds

`publish.ps1` (PowerShell 7) builds the viewer release packages into `dist/out/<version>/` (git-ignored):

```sh
pwsh dist/publish.ps1                                  # all packages below
pwsh dist/publish.ps1 -Runtime win-x64 -NoPortable     # just the Windows one
pwsh dist/publish.ps1 -Version 0.0.3-test              # override <Version> from Directory.Build.props
```

| Package | Contents |
|---|---|
| `MKWSatellite-<version>-win-x64.zip`, `-linux-x64.tar.gz`, `-osx-arm64.tar.gz` | **Self-contained**: includes the .NET runtime, nothing to install. Run `MKWSatellite`. |
| `MKWSatellite-<version>-portable.zip` | **Framework-dependent**: needs the [.NET 10 runtime](https://dotnet.microsoft.com/download/dotnet/10.0). One folder for every OS (native GLFW/cimgui for each under `runtimes/`); `MKWSatellite.exe` on Windows, `dotnet MKWSatellite.dll` anywhere. |

All are the viewer only, as a **plain folder**: no single-file bundling and no trimming, the layouts least likely to
break. Linux/macOS packages are `.tar.gz` so the launcher keeps its executable bit. Each package also contains
`README.md`, `THIRD_PARTY_NOTICES.md` and `mkwsat.local.example.json`; users set their romfs in the viewer's
**Game data** section.

Publish settings live in `src/MKWSatellite.App/MKWSatellite.App.csproj` (English resources only, no PDBs) and
`src/MKWSatellite.Formats/MKWSatellite.Formats.csproj` (no PDB). The version comes from `Directory.Build.props`.

## Releasing

1. Set `<Version>` in `Directory.Build.props`, log it in `docs/ROADMAP.md`, commit.
2. `pwsh dist/publish.ps1`, unpack a package somewhere clean and check it starts and loads a course.
3. Tag `v<version>` and push the tag; attach the archives to the release.

Only the Windows packages have been run so far; Linux and macOS are built but **untested**.
