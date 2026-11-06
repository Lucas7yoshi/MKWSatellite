#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Builds the MKWSatellite viewer release packages into dist/out/<version>/.

.DESCRIPTION
    Two kinds of package, both a plain folder (no single-file bundling, no trimming):
      MKWSatellite-<version>-<rid>        self-contained: includes the .NET runtime, nothing to install
      MKWSatellite-<version>-portable     framework-dependent: needs the .NET 10 runtime; one folder for every OS, with
                                          the native GLFW/cimgui libraries for each under runtimes/
    Files sit at the archive root (no wrapper folder). Windows packages are .zip; Linux/macOS packages are .tar.gz so
    the executable keeps its executable bit.
    See dist/README.md.

.EXAMPLE
    ./dist/publish.ps1
    ./dist/publish.ps1 -Runtime win-x64 -NoPortable
#>
param(
    # Runtimes to build self-contained packages for.
    [string[]] $Runtime = @('win-x64', 'linux-x64', 'osx-arm64'),
    # Skip the portable framework-dependent package.
    [switch] $NoPortable,
    # Defaults to <Version> in Directory.Build.props.
    [string] $Version
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src/MKWSatellite.App/MKWSatellite.App.csproj'

if (-not $Version) {
    $Version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}
$out = Join-Path $PSScriptRoot "out/$Version"
New-Item -ItemType Directory -Force $out | Out-Null
# Files shipped next to the viewer.
$extras = @('README.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md', 'mkwsat.local.example.json')

function Write-TarGz([string] $source, [string] $archive, [string] $exe) {
    # System.Formats.Tar keeps the Unix executable bit, which a tar made on Windows would otherwise lose.
    $fs = [IO.File]::Create($archive)
    $gz = [IO.Compression.GZipStream]::new($fs, [IO.Compression.CompressionLevel]::SmallestSize)
    $tar = [Formats.Tar.TarWriter]::new($gz, [Formats.Tar.TarEntryFormat]::Pax, $false)
    try {
        foreach ($f in Get-ChildItem $source -Recurse -File) {
            $rel = [IO.Path]::GetRelativePath($source, $f.FullName).Replace('\', '/')
            $entry = [Formats.Tar.PaxTarEntry]::new([Formats.Tar.TarEntryType]::RegularFile, $rel)
            $entry.Mode = if ($rel -in $exe, 'createdump') { [IO.UnixFileMode]'UserRead,UserWrite,UserExecute,GroupRead,GroupExecute,OtherRead,OtherExecute' }
                          else { [IO.UnixFileMode]'UserRead,UserWrite,GroupRead,OtherRead' }
            $entry.ModificationTime = $f.LastWriteTimeUtc
            $data = [IO.File]::OpenRead($f.FullName)
            try { $entry.DataStream = $data; $tar.WriteEntry($entry) } finally { $data.Dispose() }
        }
    }
    finally { $tar.Dispose(); $gz.Dispose(); $fs.Dispose() }
}

# (name suffix, runtime id or '' for portable, self-contained?)
$builds = @(foreach ($rid in $Runtime) { , @($rid, $rid, $true) })
if (-not $NoPortable) { $builds += , @('portable', '', $false) }

foreach ($b in $builds) {
    $suffix, $rid, $selfContained = $b
    $name = "MKWSatellite-$Version-$suffix"
    $dir = Join-Path $out $name
    $unix = $rid -and -not $rid.StartsWith('win')
    $archive = if ($unix) { "$dir.tar.gz" } else { "$dir.zip" }
    foreach ($old in $dir, $archive) { if (Test-Path $old) { Remove-Item -Recurse -Force $old } }
    Write-Host "==> $name" -ForegroundColor Cyan

    $publishArgs = @('publish', $project, '-c', 'Release', '-o', $dir, "-p:Version=$Version")
    $publishArgs += if ($selfContained) { @('-r', $rid, '--self-contained') } else { @('--no-self-contained') }
    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $name" }
    foreach ($e in $extras) { Copy-Item (Join-Path $root $e) $dir }

    if ($unix) { Write-TarGz $dir $archive 'MKWSatellite' }
    else { [IO.Compression.ZipFile]::CreateFromDirectory($dir, $archive, [IO.Compression.CompressionLevel]::SmallestSize, $false) }
    Remove-Item -Recurse -Force $dir
    Write-Host ('    {0} ({1:N1} MB)' -f (Split-Path -Leaf $archive), ((Get-Item $archive).Length / 1MB))
}
Write-Host "Output: $out"
