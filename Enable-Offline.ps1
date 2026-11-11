$ErrorActionPreference = 'Stop'
# Keeps the offline engine installed for normal launches (owner choice 2026-11-04). Installs the current build and records the
# choice in .offline-user-data/engine-enabled, so Disable-Offline.ps1 (run after every diagnostic) refreshes the engine
# instead of removing it. Undo with Disable-Offline.ps1 -Force -Forget.
$gameRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$build = Join-Path $PSScriptRoot 'build'
foreach ($pair in @(@('TESL.Offline.Access.dll', 'BepInEx/patchers/TESLOffline'), @('TESL.Offline.Engine.dll', 'BepInEx/plugins/TESLOffline'))) {
    $dir = Join-Path $gameRoot $pair[1]
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $build $pair[0]) -Destination (Join-Path $dir $pair[0]) -Force
    Remove-Item -LiteralPath (Join-Path $dir ($pair[0] + '.disabled')) -ErrorAction SilentlyContinue
    Write-Output "Enabled $($pair[1])/$($pair[0])"
}
$marker = Join-Path $gameRoot '.offline-user-data/engine-enabled'
New-Item -ItemType Directory -Path (Split-Path $marker) -Force | Out-Null
Set-Content -LiteralPath $marker -Value 'The owner keeps the offline engine on for normal launches (Enable-Offline.ps1).' -Encoding utf8
