param([switch]$EngineTests, [switch]$NativeGenericRules, [switch]$BoardDiagnostic, [switch]$RenderBoard, [switch]$BoardMatch, [string]$ModsList = '')
$ErrorActionPreference = 'Stop'
if ($BoardDiagnostic -and ($EngineTests -or $NativeGenericRules)) { throw 'BoardDiagnostic is a separate explicit test mode.' }
if ($RenderBoard -and !$BoardDiagnostic) { throw 'RenderBoard requires BoardDiagnostic.' }
if ($BoardMatch -and !$BoardDiagnostic) { throw 'BoardMatch requires BoardDiagnostic.' }
$gameRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$exe = Join-Path $gameRoot 'The Elder Scrolls Legends.exe'
if (Get-Process -Name 'The Elder Scrolls Legends' -ErrorAction SilentlyContinue) { throw 'Close the existing game before running its offline diagnostic.' }
$build = Join-Path $PSScriptRoot 'build'
foreach ($relative in @('BepInEx/patchers/TESLOffline','BepInEx/plugins/TESLOffline')) { New-Item -ItemType Directory -Path (Join-Path $gameRoot $relative) -Force | Out-Null }
Copy-Item -LiteralPath (Join-Path $build 'TESL.Offline.Access.dll') -Destination (Join-Path $gameRoot 'BepInEx/patchers/TESLOffline/TESL.Offline.Access.dll')
Copy-Item -LiteralPath (Join-Path $build 'TESL.Offline.Engine.dll') -Destination (Join-Path $gameRoot 'BepInEx/plugins/TESLOffline/TESL.Offline.Engine.dll')
$logPath = Join-Path $PSScriptRoot 'diagnostic-unity.log'
$bepLog = Join-Path $gameRoot 'BepInEx/LogOutput.log'
$offset = if (Test-Path -LiteralPath $bepLog) { (Get-Item -LiteralPath $bepLog).Length } else { 0 }
$modeFlag = if ($BoardDiagnostic) { '--tesl-offline-board-diagnostic' } elseif ($EngineTests) { '--tesl-offline-engine-tests' } else { '--tesl-offline-audit-exit' }
$arguments = @('--tesl-offline',$modeFlag,'-batchmode','-nographics','-logFile',('"' + $logPath + '"'))
if ($RenderBoard) { $arguments = $arguments | Where-Object { $_ -ne '-nographics' } }
if ($BoardMatch) { $arguments += '--tesl-offline-board-match-diagnostic' }
if ($NativeGenericRules) {
    if (!$EngineTests) { throw 'NativeGenericRules requires EngineTests.' }
    $arguments += '--tesl-offline-native-generic-tests'
}
# Optional: a mod list file (as TESL Forge writes; an empty file = no mods) instead of the player's enabled mods.
if ($ModsList) { $arguments = @('"--tesl-mods-list=' + $ModsList + '"') + $arguments }
$process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $gameRoot -WindowStyle Hidden -PassThru
$process.Id | Set-Content (Join-Path $PSScriptRoot 'diagnostic.pid')
[pscustomobject]@{Pid=$process.Id;StartedUtc=[DateTime]::UtcNow.ToString('o');BepLogOffset=$offset;Log=$logPath} | ConvertTo-Json | Set-Content (Join-Path $PSScriptRoot 'diagnostic-run.json')
Get-Content (Join-Path $PSScriptRoot 'diagnostic-run.json')
