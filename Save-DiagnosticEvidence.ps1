param([Parameter(Mandatory=$true)][ValidatePattern('^[a-z0-9-]+$')][string]$Label)
$ErrorActionPreference = 'Stop'
$record = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'diagnostic-run.json') -Raw | ConvertFrom-Json
$started = if ($record.StartedUtc -is [DateTime]) { $record.StartedUtc.ToUniversalTime() } else { [DateTimeOffset]::Parse([string]$record.StartedUtc).UtcDateTime }
$stamp = $started.ToString('yyyyMMdd-HHmmss')
$output = Join-Path $PSScriptRoot ('checkpoints/runs/' + $stamp + '-' + $Label)
if (Test-Path -LiteralPath $output) { throw 'An evidence directory already exists for this run and label.' }
New-Item -ItemType Directory -Path $output | Out-Null
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$stream = [IO.File]::Open((Join-Path $root 'BepInEx/LogOutput.log'),[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
try {
    if ($stream.Length -lt [long]$record.BepLogOffset) { throw 'The runtime log was truncated after launch.' }
    $stream.Position = [long]$record.BepLogOffset
    $reader = [IO.StreamReader]::new($stream)
    try { [IO.File]::WriteAllText((Join-Path $output 'scoped-bep.log'),$reader.ReadToEnd()) }
    finally { $reader.Dispose() }
} finally { $stream.Dispose() }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'diagnostic-run.json') -Destination $output
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'exports') -File | Where-Object {
    ($_.Name -like 'offline-practice*' -or $_.Name -eq 'offline-original-decks.tsv' -or $_.Name -like 'rule-import-*.tsv' -or $_.Name -eq 'original-default-recipe-match.tsv') -and $_.LastWriteTimeUtc -ge $started
} | Copy-Item -Destination $output
foreach ($name in @('engine-test-results.tsv','engine-command-test-results.tsv','board-diagnostic.tsv','board-diagnostic.png','board-match-diagnostic.tsv','board-match-state.tsv','board-match-animation.tsv','board-match-complete.png','localization-audit.tsv','audio-local-assets.tsv','audio-local-mappings.tsv','audio-music-before-board.tsv','audio-music-loading.tsv','audio-music-membership-repairs.tsv','audio-music-prepared.tsv','audio-music-lifecycle.tsv','audio-music-intro-routing.tsv','audio-music-after-board.tsv')) {
    $path = Join-Path $PSScriptRoot ('exports/' + $name)
    if ((Test-Path -LiteralPath $path) -and (Get-Item -LiteralPath $path).LastWriteTimeUtc -ge $started) {
        Copy-Item -LiteralPath $path -Destination $output
    }
}
Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'build/TESL.Offline.Access.dll'),(Join-Path $PSScriptRoot 'build/TESL.Offline.Engine.dll') -Algorithm SHA256 |
    Select-Object Hash,Path | Export-Csv -NoTypeInformation -LiteralPath (Join-Path $output 'build-hashes.csv')
Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' -File | Copy-Item -Destination $output
Write-Output $output



