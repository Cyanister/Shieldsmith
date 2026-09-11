<#
.SYNOPSIS
    Builds a complete, publishable release: payload, portable zip, installer,
    version-less copies for the website, checksums and the release notes body.

.DESCRIPTION
    One command so a release cannot be half-built. Running publish.ps1 and ISCC
    by hand worked, but it left the two steps free to disagree: an installer
    compiled against a stale payload looks identical to a good one.

    Every asset ships under two names.

      Shieldsmith-0.9.1-setup.exe   the archival name. A file sitting in
                                    someone's Downloads folder should say which
                                    version it is, and the changelog refers to
                                    these.
      Shieldsmith-Setup.exe         the stable name. GitHub serves
                                    /releases/latest/download/<asset> only when
                                    the asset name is identical across every
                                    release, so this is what a download button
                                    on a website points at, once, forever.

    The duplication costs upload size and nothing else: GitHub allows 2 GB per
    asset and bills no bandwidth for public repositories.

.PARAMETER SkipBuild
    Reuse the payload already in artifacts. For iterating on packaging without
    waiting for a full three-project publish.
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

# $PSScriptRoot is not populated inside the param block on PowerShell 5.1.
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $scriptRoot '..')).Path
$artifacts = Join-Path $repoRoot 'artifacts'

[xml]$props = Get-Content (Join-Path $repoRoot 'Directory.Build.props')
$version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw 'Could not read <Version> from Directory.Build.props.' }

Write-Host "Shieldsmith $version release" -ForegroundColor Cyan
Write-Host ''

# A running executable locks its own files and the publish then fails halfway,
# which is how a partial payload got into an installer once already.
Get-Process -Name Shieldsmith, shieldsmith-cli, Shieldsmith.Mcp -ErrorAction SilentlyContinue |
    Stop-Process -Force

# Clear stale assets so an old version cannot be mistaken for part of this
# release. Scoped to the release files by name: the payload folder and anything
# else in artifacts is left alone.
if (Test-Path $artifacts) {
    Get-ChildItem $artifacts -File |
        Where-Object { $_.Name -like 'Shieldsmith-*.exe' -or $_.Name -like 'Shieldsmith-*.zip' -or
                       $_.Name -eq 'SHA256SUMS.txt' -or $_.Name -eq 'RELEASE_NOTES.md' } |
        Remove-Item -Force
}

if (-not $SkipBuild) {
    & powershell -ExecutionPolicy Bypass -File (Join-Path $scriptRoot 'publish.ps1') -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'publish.ps1 failed.' }
}

# Inno Setup is not on PATH in a default install.
$iscc = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    $onPath = Get-Command iscc -ErrorAction SilentlyContinue
    if ($onPath) { $iscc = $onPath.Source }
}
if (-not $iscc) { throw 'Inno Setup 6 (ISCC.exe) was not found. Install it, or pass -SkipBuild and compile by hand.' }

Write-Host ''
Write-Host 'Compiling the installer'
& $iscc (Join-Path $scriptRoot 'Shieldsmith.iss') | Where-Object { $_ -match 'Successful compile|Error' }
if ($LASTEXITCODE -ne 0) { throw 'The installer failed to compile.' }

# The stable-name copies the website links to.
$pairs = @(
    @{ Versioned = "Shieldsmith-$version-setup.exe";            Stable = 'Shieldsmith-Setup.exe' }
    @{ Versioned = "Shieldsmith-$version-win-x64-portable.zip"; Stable = 'Shieldsmith-win-x64-portable.zip' }
)
foreach ($pair in $pairs) {
    $from = Join-Path $artifacts $pair.Versioned
    if (-not (Test-Path $from)) { throw "Expected $($pair.Versioned) was not produced." }
    Copy-Item $from (Join-Path $artifacts $pair.Stable) -Force
}

# Checksums in the format sha256sum -c expects, so they can be verified on
# Windows with Get-FileHash and on anything else with the standard tool.
$assets = $pairs | ForEach-Object { $_.Versioned; $_.Stable }
$sums = foreach ($name in $assets) {
    $file = Join-Path $artifacts $name
    "{0}  {1}" -f (Get-FileHash $file -Algorithm SHA256).Hash.ToLower(), $name
}
$sumsPath = Join-Path $artifacts 'SHA256SUMS.txt'
$sums -join "`r`n" | Set-Content -Path $sumsPath -Encoding ascii

# The release notes body, assembled from the changelog rather than hand-written
# again, so the two cannot drift.
$changelog = Get-Content (Join-Path $repoRoot 'CHANGELOG.md')
$start = ($changelog | Select-String -Pattern ("^### " + [regex]::Escape($version) + ",") | Select-Object -First 1)
$section = @()
if ($start) {
    for ($i = $start.LineNumber; $i -lt $changelog.Count; $i++) {
        if ($changelog[$i] -match '^### ') { break }
        $section += $changelog[$i]
    }
}
$whatsNew = ($section -join "`n").Trim()
if (-not $whatsNew) { $whatsNew = "See CHANGELOG.md." }

$setupSum = ($sums | Where-Object { $_ -like "*  Shieldsmith-$version-setup.exe" }) -split '\s+' | Select-Object -First 1
$zipSum = ($sums | Where-Object { $_ -like "*  Shieldsmith-$version-win-x64-portable.zip" }) -split '\s+' | Select-Object -First 1
$setupMb = [math]::Round((Get-Item (Join-Path $artifacts "Shieldsmith-$version-setup.exe")).Length / 1MB)
$zipMb = [math]::Round((Get-Item (Join-Path $artifacts "Shieldsmith-$version-win-x64-portable.zip")).Length / 1MB)

$notes = @"
Shieldsmith documents a Power Platform solution export without connecting to anything. You give
it the ``.zip`` you exported; it gives you a Word document, Markdown, an entity relationship
diagram, a chart per cloud flow, a real Visio file and Graphviz DOT. Nothing is uploaded, no
environment connection is made, and no admin rights are needed.

## Install

**Installer (recommended)** - ``Shieldsmith-$version-setup.exe``, $setupMb MB.
Installs per user, so there is no admin prompt and it works on a locked-down work machine.
Optionally adds the ``shieldsmith`` command line tool to PATH.

**Portable** - ``Shieldsmith-$version-win-x64-portable.zip``, $zipMb MB.
Unzip anywhere and run ``Shieldsmith.exe``. Nothing is written outside the folder except your
settings. Use this if you cannot run installers.

No .NET runtime to install first: the runtime ships inside the download.

### The SmartScreen warning

This build is not code signed, so Windows will show "Windows protected your PC" on first run.
Choose **More info**, then **Run anyway**. If that is not acceptable in your organisation, verify
the download against the checksum below and hand it to whoever signs off software, or build it
yourself from source: the whole thing is Apache 2.0.

### Verifying your download

``````
Get-FileHash Shieldsmith-$version-setup.exe -Algorithm SHA256
``````

| File | SHA-256 |
| --- | --- |
| ``Shieldsmith-$version-setup.exe`` | ``$setupSum`` |
| ``Shieldsmith-$version-win-x64-portable.zip`` | ``$zipSum`` |

``Shieldsmith-Setup.exe`` and ``Shieldsmith-win-x64-portable.zip`` are byte-identical copies of
the two files above, published under fixed names so that a download link never has to change.

## What is in $version

$whatsNew

---

Requires 64-bit Windows 10 or 11. Graphviz is optional; without it the built-in layout engine
draws the diagrams instead.
"@
$notesPath = Join-Path $artifacts 'RELEASE_NOTES.md'
$notes | Set-Content -Path $notesPath -Encoding utf8

Write-Host ''
Write-Host 'Release assets' -ForegroundColor Cyan
Get-ChildItem $artifacts -File |
    Where-Object { $_.Name -like 'Shieldsmith-*' -or $_.Name -eq 'SHA256SUMS.txt' -or $_.Name -eq 'RELEASE_NOTES.md' } |
    Sort-Object Name |
    ForEach-Object { Write-Host ("  {0,-46} {1,6:N1} MB" -f $_.Name, ($_.Length / 1MB)) }

Write-Host ''
Write-Host 'To publish:' -ForegroundColor Cyan
Write-Host "  1. git tag v$version  (if it does not exist yet) and push it."
Write-Host '  2. github.com/Cyanister/Shieldsmith/releases/new, choose the tag,'
Write-Host "     paste artifacts\RELEASE_NOTES.md as the body, and attach all four files."
Write-Host '  3. The website download button then resolves, permanently, to:'
Write-Host '       https://github.com/Cyanister/Shieldsmith/releases/latest/download/Shieldsmith-Setup.exe'
Write-Host ''
Write-Host 'Unsigned builds trigger SmartScreen. build\sign.ps1 is ready when a certificate is.'
