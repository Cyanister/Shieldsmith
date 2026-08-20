<#
.SYNOPSIS
    Publishes Shieldsmith as a self-contained win-x64 payload, then packs it as
    a portable zip and writes the version file the installer script reads.

.DESCRIPTION
    All three executables publish into ONE folder so the .NET and WPF runtime is
    shipped once rather than three times. Published as a folder rather than as
    single-file executables: single-file would re-duplicate the runtime per exe
    and take the payload from about 180 MB to about 440 MB, which is a lot to
    ask someone to download for a documentation tool.

    Self-contained is deliberate. A .NET runtime prerequisite is one of the most
    common complaints against PowerDocu and is exactly the barrier this is meant
    to remove.

    Publishing runs single-threaded (-m:1). On the portable SSD the parallel file
    copies race and fail with MSB3026/MSB4018.
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$OutputRoot,
    [switch]$SkipZip
)

$ErrorActionPreference = 'Stop'

# $PSScriptRoot is not populated inside the param block on PowerShell 5.1,
# so the paths are resolved here instead.
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $scriptRoot '..')).Path
if (-not $OutputRoot) { $OutputRoot = Join-Path $repoRoot 'artifacts' }

$payload = Join-Path $OutputRoot 'Shieldsmith'
if (Test-Path $payload) { Remove-Item -LiteralPath $payload -Recurse -Force }
New-Item -ItemType Directory -Force -Path $payload | Out-Null

# The version is read from Directory.Build.props so it is stated in exactly one
# place. The installer script reads the file this writes, which is why its
# version cannot drift from the application's again.
[xml]$props = Get-Content (Join-Path $repoRoot 'Directory.Build.props')
$version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw 'Could not read <Version> from Directory.Build.props.' }
Write-Host "Shieldsmith $version, $Runtime, $Configuration"
Write-Host ''

$projects = @(
    @{ Path = 'src\Shieldsmith.App'; Produces = 'Shieldsmith.exe' }
    @{ Path = 'src\Shieldsmith.Cli'; Produces = 'shieldsmith-cli.exe' }
    @{ Path = 'src\Shieldsmith.Mcp'; Produces = 'Shieldsmith.Mcp.exe' }
)

foreach ($project in $projects) {
    Write-Host "Publishing $($project.Path)"
    & dotnet publish (Join-Path $repoRoot $project.Path) `
        --configuration $Configuration `
        --runtime $Runtime `
        --self-contained `
        -p:PublishSingleFile=false `
        -p:DebugType=none `
        -p:DebugSymbols=false `
        --output $payload `
        --nologo -v minimal -m:1
    if ($LASTEXITCODE -ne 0) { throw "Publish failed for $($project.Path)." }

    $exe = Join-Path $payload $project.Produces
    if (-not (Test-Path $exe)) { throw "Expected $exe was not produced." }
}

# Typing "shieldsmith" has to keep working even though the executable had to be
# renamed to sit beside Shieldsmith.exe in one folder.
@'
@echo off
rem Shieldsmith command line. Forwards to the executable beside this shim so
rem that "shieldsmith" works on PATH despite Windows file names being
rem case-insensitive, which stops shieldsmith.exe living next to Shieldsmith.exe.
"%~dp0shieldsmith-cli.exe" %*
'@ | Set-Content -Path (Join-Path $payload 'shieldsmith.cmd') -Encoding ascii

foreach ($doc in 'README.md', 'CHANGELOG.md', 'LICENSE', 'NOTICE') {
    Copy-Item (Join-Path $repoRoot $doc) $payload -Force
}

# Debug symbols and IntelliSense XML have no business in a public download.
Get-ChildItem $payload -Recurse -Include *.pdb, *.xml |
    Where-Object { $_.Name -ne 'Shieldsmith.exe.config' } |
    Remove-Item -Force -ErrorAction SilentlyContinue

$size = (Get-ChildItem $payload -Recurse -File | Measure-Object -Property Length -Sum).Sum
Write-Host ''
Write-Host ("Payload: {0} files, {1:N0} MB" -f `
    (Get-ChildItem $payload -Recurse -File).Count, ($size / 1MB))

# The installer reads this so its version can never go stale again.
$versionFile = Join-Path $scriptRoot 'version.iss'
"#define AppVersion `"$version`"" | Set-Content -Path $versionFile -Encoding ascii
Write-Host "Wrote $versionFile"

if (-not $SkipZip) {
    $zip = Join-Path $OutputRoot "Shieldsmith-$version-win-x64-portable.zip"
    if (Test-Path $zip) { Remove-Item -LiteralPath $zip -Force }
    # Compress-Archive on PowerShell 5.1 is slow and mangles some paths; the
    # framework call is both faster and exact.
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $payload, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $true)
    Write-Host ("Portable zip: {0} ({1:N0} MB)" -f $zip, ((Get-Item $zip).Length / 1MB))
}

Write-Host ''
Write-Host 'Next:'
Write-Host '  1. Sign every .exe in the payload before publishing it:'
Write-Host '       build\sign.ps1 -CertificateThumbprint <thumbprint>'
Write-Host '  2. Build the installer:'
Write-Host '       iscc build\Shieldsmith.iss'
Write-Host '  3. Sign the installer itself as well.'
Write-Host 'Unsigned builds trigger SmartScreen, which is the first thing a new user hits.'
