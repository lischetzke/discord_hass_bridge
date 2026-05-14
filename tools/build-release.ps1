#!/usr/bin/env pwsh
# Builds a single release-ready DiscordHass.exe under release/ along with a SHA-256 sidecar.
#
# Usage:
#   pwsh tools/build-release.ps1                                  # uses default version
#   pwsh tools/build-release.ps1 -Version 1.2.3                   # explicit version
#   pwsh tools/build-release.ps1 -Version 1.2.3 -SkipTests        # skip the test pass
#   pwsh tools/build-release.ps1 -Version 1.2.3 -Runtime win-arm64 # cross-target

[CmdletBinding()]
param(
    [string]$Version       = '0.1.0',
    [string]$Configuration = 'Release',
    [string]$Runtime       = 'win-x64',
    [switch]$SkipTests,
    [switch]$KeepIntermediate
)

$ErrorActionPreference = 'Stop'

$repoRoot   = Split-Path -Parent $PSScriptRoot
$projectDir = Join-Path $repoRoot 'src/DiscordHass'
$project    = Join-Path $projectDir 'DiscordHass.csproj'
$tests      = Join-Path $repoRoot 'tests/DiscordHass.Tests/DiscordHass.Tests.csproj'
$releaseDir = Join-Path $repoRoot 'release'

Push-Location $repoRoot
try {
    Write-Host "DiscordHass release build" -ForegroundColor Cyan
    Write-Host "  Repo:    $repoRoot"
    Write-Host "  Version: $Version"
    Write-Host "  Config:  $Configuration"
    Write-Host "  Runtime: $Runtime"
    Write-Host ""

    if (-not $SkipTests) {
        Write-Host "==> Running tests"
        & dotnet test $tests --configuration $Configuration --nologo --verbosity minimal
        if ($LASTEXITCODE -ne 0) { throw "Tests failed (exit $LASTEXITCODE)" }
        Write-Host ""
    }

    # Wipe the intermediate publish dir so we don't accidentally pick up stale bits.
    $publishDir = Join-Path $projectDir "bin/$Configuration/net10.0-windows/$Runtime/publish"
    if (Test-Path $publishDir) {
        Remove-Item -Recurse -Force $publishDir
    }

    Write-Host "==> Publishing self-contained single-file"
    $publishArgs = @(
        'publish'
        $project
        '-c', $Configuration
        '-r', $Runtime
        '--nologo'
        "-p:Version=$Version"
        "-p:FileVersion=$Version"
        "-p:AssemblyVersion=$($Version -replace '-.*','').0"
        "-p:InformationalVersion=$Version"
    )
    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { throw "Publish failed (exit $LASTEXITCODE)" }
    Write-Host ""

    $sourceExe = Join-Path $publishDir 'DiscordHass.exe'
    if (-not (Test-Path $sourceExe)) {
        throw "Expected output not found: $sourceExe"
    }

    $null = New-Item -ItemType Directory -Force -Path $releaseDir
    $destName    = "DiscordHass-v$Version-$Runtime.exe"
    $destExe     = Join-Path $releaseDir $destName
    $destSha256  = "$destExe.sha256"

    if (Test-Path $destExe)    { Remove-Item -Force $destExe }
    if (Test-Path $destSha256) { Remove-Item -Force $destSha256 }

    Copy-Item -Force $sourceExe $destExe

    $hash   = (Get-FileHash $destExe -Algorithm SHA256).Hash.ToLower()
    $sizeMb = [Math]::Round((Get-Item $destExe).Length / 1MB, 1)

    # GitHub release sha256-sidecar convention: "<hash>  <filename>" + newline.
    "$hash  $destName`n" | Set-Content -Encoding ascii -NoNewline -Path $destSha256

    if (-not $KeepIntermediate) {
        Remove-Item -Recurse -Force $publishDir
    }

    Write-Host "==> Release artifact ready" -ForegroundColor Green
    Write-Host "    Path:     $destExe"
    Write-Host "    Size:     $sizeMb MB"
    Write-Host "    SHA-256:  $hash"
    Write-Host "    Sidecar:  $destSha256"
    Write-Host ""
    Write-Host "Upload $destName and $($destName).sha256 to a GitHub release for tag v$Version." -ForegroundColor Cyan
}
finally {
    Pop-Location
}
