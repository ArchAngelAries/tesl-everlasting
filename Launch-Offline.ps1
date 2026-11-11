param([switch]$NoWait)
$ErrorActionPreference = 'Stop'
$gameRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if (Get-Process -Name 'The Elder Scrolls Legends' -ErrorAction SilentlyContinue) { throw 'Close the running game before starting offline play.' }
$exe = Join-Path $gameRoot 'The Elder Scrolls Legends.exe'
foreach ($relative in @('BepInEx/patchers/TESLOffline','BepInEx/plugins/TESLOffline')) { New-Item -ItemType Directory -Path (Join-Path $gameRoot $relative) -Force | Out-Null }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'build/TESL.Offline.Access.dll') -Destination (Join-Path $gameRoot 'BepInEx/patchers/TESLOffline/TESL.Offline.Access.dll')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'build/TESL.Offline.Engine.dll') -Destination (Join-Path $gameRoot 'BepInEx/plugins/TESLOffline/TESL.Offline.Engine.dll')
$logPath = Join-Path $PSScriptRoot 'offline-play-unity.log'
$bepLog = Join-Path $gameRoot 'BepInEx/LogOutput.log'
$offset = if (Test-Path -LiteralPath $bepLog) { (Get-Item -LiteralPath $bepLog).Length } else { 0 }
# The original client's own menus on local services (native offline mode); window size, fullscreen and quality come from the
# game's settings and the F10 Display / Quality preset options, so nothing is forced here.
$arguments = @('--tesl-offline-native','-logFile',('"' + $logPath + '"'))
# Visible interactive gameplay is the purpose of this launcher.
$process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $gameRoot -WindowStyle Normal -PassThru
[pscustomobject]@{Pid=$process.Id;StartedUtc=[DateTime]::UtcNow.ToString('o');BepLogOffset=$offset;Log=$logPath;Mode='offline-native-play'} | ConvertTo-Json | Set-Content (Join-Path $PSScriptRoot 'diagnostic-run.json')
Get-Content (Join-Path $PSScriptRoot 'diagnostic-run.json')
if (!$NoWait) {
    try { $process.WaitForExit() }
    finally { if (!(Get-Process -Name 'The Elder Scrolls Legends' -ErrorAction SilentlyContinue)) { & (Join-Path $PSScriptRoot 'Disable-Offline.ps1') } }
}
