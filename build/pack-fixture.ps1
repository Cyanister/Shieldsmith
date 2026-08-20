<#
.SYNOPSIS
    Repacks data/ContosoTravel_sample into data/ContosoTravel_sample.zip.

.DESCRIPTION
    The tests read the zip, not the folder, so the folder is the source of truth
    and this turns it back into an export. Run it after editing the fixture.

    Entry names are written with forward slashes, which is what a real export
    uses at the top level. The backslash entry names that some real exports use
    live inside the .msapp, which is a single file here and is copied verbatim.
#>
[CmdletBinding()]
param([string]$Root)

$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $Root) { $Root = (Resolve-Path (Join-Path $scriptRoot '..')).Path }

$source = Join-Path $Root 'data\ContosoTravel_sample'
$target = Join-Path $Root 'data\ContosoTravel_sample.zip'
if (-not (Test-Path $source)) { throw "Fixture folder not found: $source" }

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

if (Test-Path $target) { Remove-Item -LiteralPath $target -Force }

$archive = [System.IO.Compression.ZipFile]::Open($target, 'Create')
try {
    $prefix = (Resolve-Path $source).Path.TrimEnd('\') + '\'
    foreach ($file in Get-ChildItem $source -Recurse -File | Sort-Object FullName) {
        $relative = $file.FullName.Substring($prefix.Length).Replace('\', [char]47)
        $entry = $archive.CreateEntry($relative, [System.IO.Compression.CompressionLevel]::Optimal)
        $stream = $entry.Open()
        try { [System.IO.File]::OpenRead($file.FullName).CopyTo($stream) } finally { $stream.Dispose() }
    }
} finally {
    $archive.Dispose()
}

$check = [System.IO.Compression.ZipFile]::OpenRead($target)
$count = $check.Entries.Count
$check.Dispose()
Write-Host ("Packed {0} entries into {1} ({2:N1} KB)" -f `
    $count, $target, ((Get-Item $target).Length / 1KB))
