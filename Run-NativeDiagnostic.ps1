param([int]$Seconds = 150, [string]$Seed = '', [string]$Menu = '', [string]$ModsList = '')
$ErrorActionPreference = 'Stop'
# Hidden native first-run verification: original boot/menus against local services,
# batch mode with graphics, no window, no user input.
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
$arguments = @('--tesl-offline-native','--tesl-offline-native-diagnostic','-batchmode','-screen-width','1280','-screen-height','720','-logFile',('"' + $logPath + '"'))
if ($Menu) { $arguments = @('--tesl-offline-native-menu=' + $Menu) + $arguments }
if ($Seed) { $arguments = @('--tesl-offline-native-seed=' + $Seed) + $arguments }
# Optional: a mod list file (as TESL Forge writes) instead of the player's enabled mods; the player's list is not changed.
if ($ModsList) { $arguments = @('"--tesl-mods-list=' + $ModsList + '"') + $arguments }
$process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $gameRoot -WindowStyle Hidden -PassThru
$process.Id | Set-Content (Join-Path $PSScriptRoot 'diagnostic.pid')
[pscustomobject]@{Pid=$process.Id;StartedUtc=[DateTime]::UtcNow.ToString('o');BepLogOffset=$offset;Log=$logPath;Mode='native-diagnostic'} | ConvertTo-Json | Set-Content (Join-Path $PSScriptRoot 'diagnostic-run.json')
Get-Content (Join-Path $PSScriptRoot 'diagnostic-run.json')
