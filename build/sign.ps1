<#
.SYNOPSIS
    Authenticode-signs every Shieldsmith executable, and the installer if it has
    been built.

.DESCRIPTION
    Signing is not optional for a public release. An unsigned download triggers
    SmartScreen, which shows the user a red warning and hides the Run button
    behind "More info". That is the single biggest barrier between a stranger
    and a working install, and it is a live complaint against PowerDocu.

    Two routes:

      Azure Trusted Signing (recommended, no certificate to store)
        Costs a few pounds a month, no hardware token, and reputation builds
        against Microsoft's own root rather than a fresh certificate. Requires
        the Trusted Signing dlib and an Azure account:
          signtool sign /v /debug /fd SHA256 /tr <endpoint> /td SHA256 `
            /dlib <path>\Azure.CodeSigning.Dlib.dll /dmdf <metadata.json> <files>

      An OV or EV code signing certificate you already hold
        Pass its thumbprint to this script. EV certificates carry SmartScreen
        reputation immediately; OV certificates have to earn it over time and
        downloads may still be warned about for a while.

.EXAMPLE
    build\sign.ps1 -CertificateThumbprint A1B2C3...
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$CertificateThumbprint,
    [string]$TimestampUrl = 'http://timestamp.digicert.com',
    [string]$PayloadRoot
)

$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $scriptRoot '..')).Path
if (-not $PayloadRoot) { $PayloadRoot = Join-Path $repoRoot 'artifacts' }

# signtool is not on PATH by default; it ships with the Windows SDK.
$signtool = Get-Command signtool.exe -ErrorAction SilentlyContinue
if (-not $signtool) {
    $candidates = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Recurse `
        -Filter signtool.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\' } |
        Sort-Object FullName -Descending
    if (-not $candidates) {
        throw 'signtool.exe not found. Install the Windows SDK, or use Azure Trusted Signing.'
    }
    $signtool = $candidates[0]
}
Write-Host "Using $($signtool.Source ?? $signtool.FullName)"

$targets = @(Get-ChildItem $PayloadRoot -Recurse -Include *.exe -File)
if ($targets.Count -eq 0) { throw "No executables under $PayloadRoot. Run build\publish.ps1 first." }

foreach ($target in $targets) {
    Write-Host "Signing $($target.Name)"
    & ($signtool.Source ?? $signtool.FullName) sign `
        /sha1 $CertificateThumbprint `
        /fd SHA256 /td SHA256 /tr $TimestampUrl `
        /d 'Shieldsmith' `
        $target.FullName
    if ($LASTEXITCODE -ne 0) { throw "Signing failed for $($target.Name)." }
}

Write-Host ''
Write-Host "Signed $($targets.Count) file(s). Verify with:"
Write-Host '  signtool verify /pa /v <file>'
Write-Host 'Sign the installer too, after iscc has built it.'
