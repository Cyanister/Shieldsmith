<#
.SYNOPSIS
    Publishes Shieldsmith as self-contained win-x64 executables.

.DESCRIPTION
    Produces three single-file executables that run without a .NET runtime
    installed, which is the point: PowerDocu's runtime dependency is one of its
    most common user complaints.

    Publishing runs single-threaded (-m:1) deliberately. On the portable SSD the
    parallel file copies race and fail with MSB3026/MSB4018.
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$OutputRoot
)

$ErrorActionPreference = 'Stop'

# $PSScriptRoot is not populated inside the param block on PowerShell 5.1,
# so the paths are resolved here instead.
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot '..')
if (-not $OutputRoot) { $OutputRoot = Join-Path $repoRoot 'artifacts\publish' }

$projects = @(
    @{ Name = 'app'; Path = 'src\Shieldsmith.App'; Produces = 'Shieldsmith.exe' }
    @{ Name = 'cli'; Path = 'src\Shieldsmith.Cli'; Produces = 'shieldsmith.exe' }
    @{ Name = 'mcp'; Path = 'src\Shieldsmith.Mcp'; Produces = 'Shieldsmith.Mcp.exe' }
)

foreach ($project in $projects) {
    $output = Join-Path $OutputRoot $project.Name
    Write-Host "Publishing $($project.Path) -> $output"
    & dotnet publish (Join-Path $repoRoot $project.Path) `
        --configuration $Configuration `
        --runtime $Runtime `
        --self-contained `
        -p:PublishSingleFile=true `
        --output $output `
        --nologo -v minimal -m:1
    if ($LASTEXITCODE -ne 0) { throw "Publish failed for $($project.Path)." }

    $exe = Join-Path $output $project.Produces
    if (-not (Test-Path $exe)) { throw "Expected $exe was not produced." }
    $sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
    Write-Host "  $($project.Produces): $sizeMb MB"
}

Write-Host ''
Write-Host 'Published. Before a public release, sign each executable:'
Write-Host '  signtool sign /tr <timestamp-url> /td sha256 /fd sha256 <exe>'
Write-Host 'Unsigned builds trigger SmartScreen, which is a live complaint against PowerDocu.'
