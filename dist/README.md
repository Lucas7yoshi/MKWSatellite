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
break. Files sit at the archive root (no wrapper folder). Linux/macOS packages are `.tar.gz` so the launcher keeps
its executable bit. Each package also contains
`README.md`, `LICENSE`, `THIRD_PARTY_NOTICES.md` and `mkwsat.local.example.json`; users set their romfs in the viewer's
**Game data** section.

Publish settings live in `src/MKWSatellite.App/MKWSatellite.App.csproj` (English resources only, no PDBs) and
`src/MKWSatellite.Formats/MKWSatellite.Formats.csproj` (no PDB). The version comes from `Directory.Build.props`.

## CI (`.github/workflows/build.yml`)

- Every push to `main`, pull request and manual run: build and test (Debug and Release), then publish the viewer
  framework-dependent for win-x64, linux-x64, osx-x64 and osx-arm64 as downloadable workflow artifacts (GitHub zips
  artifacts and drops the executable bit, so on Linux/macOS run `chmod +x MKWSatellite` or `dotnet MKWSatellite.dll`).
- Pushing a `v*` tag also runs `dist/publish.ps1` with the tag's version and creates a **draft** GitHub release with
  its packages and notes from `dist/release-notes.sh`: **What's changed** (each commit's subject and message since the
  previous `v*` tag, attribution trailers dropped) and a linked **Commits** list. Preview them locally with
  `bash dist/release-notes.sh HEAD`. Edit and publish the draft by hand.

## Releasing

1. Set `<Version>` in `Directory.Build.props`, log it in `docs/ROADMAP.md`, commit and push.
2. Optionally `pwsh dist/publish.ps1` locally, unpack a package somewhere clean and check it starts and loads a course.
3. Tag `v<version>` and push the tag; CI builds the packages into a draft release.

Only the Windows packages have been run so far; Linux and macOS are built but **untested**.
