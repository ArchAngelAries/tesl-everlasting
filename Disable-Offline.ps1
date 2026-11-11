param([switch]$Force, [switch]$Forget)
$ErrorActionPreference = 'Stop'
$gameRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
# Owner choice (Enable-Offline.ps1): the engine stays on for normal launches. After a diagnostic the current build is
# reinstalled instead of removed. -Force disables anyway (this time); -Forget also drops the choice.
$marker = Join-Path $gameRoot '.offline-user-data/engine-enabled'
if ($Forget) { Remove-Item -LiteralPath $marker -ErrorAction SilentlyContinue }
if ((Test-Path -LiteralPath $marker) -and -not $Force) {
    & (Join-Path $PSScriptRoot 'Enable-Offline.ps1') | Out-Null
    Write-Output 'Kept the offline engine on (owner choice; Disable-Offline.ps1 -Force -Forget to turn it off).'
    return
}
$files = @(
    'BepInEx/patchers/TESLOffline/TESL.Offline.Access.dll',
    'BepInEx/plugins/TESLOffline/TESL.Offline.Engine.dll'
)
foreach ($relative in $files) {
    $source = Join-Path $gameRoot $relative
    if (Test-Path -LiteralPath $source) {
        Move-Item -LiteralPath $source -Destination ($source + '.disabled') -Force
        Write-Output "Disabled $relative"
    }
}
