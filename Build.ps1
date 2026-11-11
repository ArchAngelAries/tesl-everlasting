param([switch]$BootstrapOnly)
$ErrorActionPreference = 'Stop'
# Guard (2026-10-15): never rebuild while the tracked diagnostic still runs; save its evidence and stop it first, so the saved
# snapshot's build hashes match what was actually tested.
$runRecord = Join-Path $PSScriptRoot 'diagnostic-run.json'
if (Test-Path $runRecord) {
    $tracked = Get-Content $runRecord -Raw | ConvertFrom-Json
    # the recorded PID only counts while it is still the game (Windows reuses PIDs, e.g. for svchost)
    if ($tracked.Pid -and (Get-Process -Id $tracked.Pid -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -eq 'The Elder Scrolls Legends' })) {
        throw ('Tracked diagnostic PID ' + $tracked.Pid + ' is still running: run Save-DiagnosticEvidence.ps1 and Stop-Diagnostic.ps1 before building.')
    }
}
$gameRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$compiler = 'C:/Windows/Microsoft.NET/Framework/v4.0.30319/csc.exe'
$managed = Join-Path $gameRoot 'The Elder Scrolls Legends_Data/Managed'
$core = Join-Path $gameRoot 'BepInEx/core'
$outDir = Join-Path $PSScriptRoot 'build'
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
& (Join-Path $PSScriptRoot 'Publicize-Reference.ps1')
$frameworkRefs = @('/nostdlib+',('/reference:' + (Join-Path $managed 'mscorlib.dll')),('/reference:' + (Join-Path $managed 'System.dll')),('/reference:' + (Join-Path $managed 'System.Core.dll')))
$patchArgs = @('/nologo','/noconfig','/target:library','/platform:x86','/optimize+',('/out:' + (Join-Path $outDir 'TESL.Offline.Access.dll')),('/reference:' + (Join-Path $core 'Mono.Cecil.dll')),(Join-Path $PSScriptRoot 'OfflineAccessPatcher.cs')) + $frameworkRefs
& $compiler @patchArgs
if ($LASTEXITCODE -ne 0) { throw 'Preloader compile failed.' }
$refs = @((Join-Path $PSScriptRoot 'ref/Assembly-CSharp.dll'),(Join-Path $core 'BepInEx.dll'),(Join-Path $core '0Harmony.dll'))
$refs += Get-ChildItem $managed -Filter '*.dll' | Where-Object { $_.Name -ne 'Assembly-CSharp.dll' -and $_.Name -ne 'mscorlib.dll' -and $_.Name -notlike 'System*' -and $_.Name -notlike 'Mono*' } | ForEach-Object { $_.FullName }
$argsList = @('/nologo','/noconfig','/target:library','/platform:x86','/optimize+',('/out:' + (Join-Path $outDir 'TESL.Offline.Engine.dll'))) + $frameworkRefs
foreach ($ref in $refs) { $argsList += '/reference:' + $ref }
$sources = Get-ChildItem $PSScriptRoot -Filter '*.cs' | Where-Object { $_.Name -ne 'OfflineAccessPatcher.cs' -and $_.Name -notlike '*Harness*' }
if ($BootstrapOnly) { $sources = $sources | Where-Object { $_.Name -eq 'OfflineBootstrapPlugin.cs' } }
foreach ($source in $sources) { $argsList += $source.FullName }
& $compiler @argsList
if ($LASTEXITCODE -ne 0) { throw 'Offline plugin compile failed.' }
Get-FileHash @((Join-Path $outDir 'TESL.Offline.Access.dll'),(Join-Path $outDir 'TESL.Offline.Engine.dll')) -Algorithm SHA256 | Select-Object Hash,@{Name='File';Expression={Split-Path $_.Path -Leaf}} | Format-Table -AutoSize
