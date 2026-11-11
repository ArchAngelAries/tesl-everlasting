$ErrorActionPreference = 'Stop'
$recordPath = Join-Path $PSScriptRoot 'diagnostic-run.json'
if (!(Test-Path -LiteralPath $recordPath)) { return }
$record = Get-Content -LiteralPath $recordPath | ConvertFrom-Json
$process = Get-CimInstance Win32_Process -Filter ('ProcessId=' + [int]$record.Pid)
if (!$process) { return }
$gameRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$exePath = Join-Path $gameRoot 'The Elder Scrolls Legends.exe'
$recordTime = if ($record.StartedUtc -is [DateTime]) { $record.StartedUtc.ToUniversalTime() } else { [DateTimeOffset]::Parse($record.StartedUtc).UtcDateTime }
if ($process.ExecutablePath -ne $exePath -or $process.CommandLine -notmatch '--tesl-offline' -or
    $process.CommandLine -notmatch '--tesl-offline-(audit-exit|engine-tests|board-diagnostic|play|native-diagnostic)' -or
    [Math]::Abs(($process.CreationDate.ToUniversalTime() - $recordTime).TotalSeconds) -gt 10) {
    throw 'Recorded process is not the diagnostic launched by this project.'
}
Stop-Process -Id $process.ProcessId -Force
Write-Output ('Stopped tracked offline diagnostic ' + $process.ProcessId)
