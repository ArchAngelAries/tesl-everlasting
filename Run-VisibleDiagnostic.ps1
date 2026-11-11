param([int]$Seconds = 90, [string]$Seed = 'native-xp-player-state', [string]$Menu = '', [int]$Width = 1920, [int]$Height = 1080)
$ErrorActionPreference = 'Stop'
# Visible native diagnostic (owner-approved runs only): like Run-NativeDiagnostic but rendered in a normal window, so frame
# rate and visuals are real. Uses a seeded test profile, never the player's; stops the game after $Seconds.
$gameRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$exe = Join-Path $gameRoot 'The Elder Scrolls Legends.exe'
if (Get-Process -Name 'The Elder Scrolls Legends' -ErrorAction SilentlyContinue) { throw 'Close the existing game before running a visible diagnostic.' }
$build = Join-Path $PSScriptRoot 'build'
foreach ($relative in @('BepInEx/patchers/TESLOffline','BepInEx/plugins/TESLOffline')) { New-Item -ItemType Directory -Path (Join-Path $gameRoot $relative) -Force | Out-Null }
Copy-Item -LiteralPath (Join-Path $build 'TESL.Offline.Access.dll') -Destination (Join-Path $gameRoot 'BepInEx/patchers/TESLOffline/TESL.Offline.Access.dll')
Copy-Item -LiteralPath (Join-Path $build 'TESL.Offline.Engine.dll') -Destination (Join-Path $gameRoot 'BepInEx/plugins/TESLOffline/TESL.Offline.Engine.dll')
$logPath = Join-Path $PSScriptRoot 'diagnostic-unity.log'
$arguments = @('--tesl-offline-native','--tesl-offline-native-diagnostic','-screen-fullscreen','0','-screen-width',"$Width",'-screen-height',"$Height",'-logFile',('"' + $logPath + '"'))
if ($Menu) { $arguments = @('--tesl-offline-native-menu=' + $Menu) + $arguments }
if ($Seed) { $arguments = @('--tesl-offline-native-seed=' + $Seed) + $arguments }
$process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $gameRoot -PassThru
$process.Id | Set-Content (Join-Path $PSScriptRoot 'diagnostic.pid')
try { Wait-Process -Id $process.Id -Timeout $Seconds -ErrorAction Stop } catch { }
if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force; $process.WaitForExit(15000) | Out-Null }
& (Join-Path $PSScriptRoot 'Disable-Offline.ps1') | Out-Null
Select-String -Path $logPath -Pattern 'OFFLINE GRAPHICS' | ForEach-Object { $_.Line }
