param([string] $OutputDirectory = '.local-e2e/private-fixtures')
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot/..").Path
$files = @(
 'schema/local-baseline.sql','schema/local-seed.sql','schema/event-feed-calendar-current.sql',
 'schema/event-feed-tables-before.sql','schema/event-feed-functions-before.sql',
 'migrations/001_bracket_processing.sql','migrations/002_bracket_matchup_keys.sql',
 'migrations/20261006_user_startgg_profile.sql','migrations/20261009_user_authentication.sql',
 'migrations/20261010_registration_bracket_e2e.sql','migrations/20261020_shared_event_feeds.sql'
)
$output = [IO.Path]::GetFullPath((Join-Path $repo $OutputDirectory))
New-Item -ItemType Directory -Path $output -Force | Out-Null
$zipPath = Join-Path $output 'private-database-fixtures.zip'
$stream = [IO.File]::Open($zipPath,[IO.FileMode]::Create)
$zip = [IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($relative in $files) {
        $source = Join-Path $repo ('database/' + $relative)
        if (!(Test-Path -LiteralPath $source)) { throw "Missing private fixture: $relative" }
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$source,'database/'+$relative,[IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $zip.Dispose(); $stream.Dispose() }
$contents = @{}
foreach ($relative in $files) { $contents['database/'+$relative] = [IO.File]::ReadAllText((Join-Path $repo ('database/'+$relative))) }
# Compress together so repeated contract definitions do not exhaust GitHub's secret size limit.
$bytes = [Text.Encoding]::UTF8.GetBytes(($contents | ConvertTo-Json -Compress))
$buffer = [IO.MemoryStream]::new()
$compressor = [IO.Compression.BrotliStream]::new($buffer,[IO.Compression.CompressionLevel]::SmallestSize,$true)
try { $compressor.Write($bytes,0,$bytes.Length) } finally { $compressor.Dispose() }
$encoded = [Convert]::ToBase64String($buffer.ToArray())
$buffer.Dispose()
if ($encoded.Length -gt 48000) { throw 'Fixture bundle exceeds the repository-secret size budget; use private artifact storage instead.' }
[IO.File]::WriteAllText((Join-Path $output 'private-database-fixtures.base64.txt'),$encoded)
Write-Host 'Created the private ZIP and base64 file. Do not commit either file or publish them as workflow artifacts.'
