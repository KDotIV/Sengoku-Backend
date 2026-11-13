param([string] $ZipPath, [switch] $FromEnvironment)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path "$PSScriptRoot/..").Path
if ($FromEnvironment) {
    if (!$env:PRIVATE_DATABASE_FIXTURES_B64) { throw 'Configure PRIVATE_DATABASE_FIXTURES_B64 for trusted integration runs.' }
    $compressed=[IO.MemoryStream]::new([Convert]::FromBase64String($env:PRIVATE_DATABASE_FIXTURES_B64))
    $decompressor=[IO.Compression.BrotliStream]::new($compressed,[IO.Compression.CompressionMode]::Decompress)
    $reader=[IO.StreamReader]::new($decompressor)
    try { $contents=$reader.ReadToEnd() | ConvertFrom-Json -AsHashtable } finally { $reader.Dispose(); $decompressor.Dispose(); $compressed.Dispose() }
    $allowed=[IO.Path]::GetFullPath((Join-Path $repo 'database'))+[IO.Path]::DirectorySeparatorChar
    foreach ($entry in $contents.GetEnumerator()) {
        $target=[IO.Path]::GetFullPath((Join-Path $repo $entry.Key))
        if (!$target.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetExtension($target) -ne '.sql') { throw 'Unexpected private fixture path.' }
        New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
        [IO.File]::WriteAllText($target,$entry.Value)
    }
    Write-Host 'Private database fixtures imported locally.'
    return
} else { $stream=[IO.File]::OpenRead((Resolve-Path -LiteralPath $ZipPath)) }
$zip=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Read)
try {
    $allowed=[IO.Path]::GetFullPath((Join-Path $repo 'database'))+[IO.Path]::DirectorySeparatorChar
    foreach($entry in $zip.Entries) {
        $target=[IO.Path]::GetFullPath((Join-Path $repo $entry.FullName))
        if (!$target.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetExtension($target) -ne '.sql') { throw 'Unexpected private fixture path.' }
        New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,$target,$true)
    }
} finally { $zip.Dispose(); $stream.Dispose() }
Write-Host 'Private database fixtures imported locally.'
