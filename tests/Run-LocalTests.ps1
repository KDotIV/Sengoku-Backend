param(
    [ValidateSet('Auto','Native','Docker')][string] $DatabaseRuntime = 'Auto',
    [string] $PostgresBin,
    [string] $DatabaseSettings,
    [ValidateSet('Debug','Release')][string] $Configuration = 'Debug',
    [switch] $Serve,
    [switch] $NoBuild
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot/..").Path
Push-Location $repo
$ownedCluster = $null
$container = $null
$oldPgPassword = $env:PGPASSWORD
$oldDockerPassword = $env:POSTGRES_PASSWORD
function Invoke-PgControl([string[]] $ControlArguments, [string] $LogPrefix) {
    # Avoid a native PowerShell pipeline inheriting the daemon's output handles on Windows.
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = (Get-Command (Join-Path $PostgresBin 'pg_ctl')).Source
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    foreach ($argument in $ControlArguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    try {
        if (!$process.WaitForExit(60000)) { throw 'PostgreSQL control command timed out.' }
        if ($process.ExitCode -ne 0) { throw "PostgreSQL control command failed. Inspect $LogPrefix" }
    } finally { $process.Dispose() }
}
try {
    foreach ($file in @('database/schema/local-baseline.sql','database/schema/local-seed.sql','database/schema/event-feed-calendar-current.sql')) {
        if (!(Test-Path -LiteralPath $file)) { throw 'Private database fixtures are missing. Import the private fixture bundle described in docs/local-testing.md.' }
    }
    $run = Join-Path $repo ('.local-e2e/run-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $run -Force | Out-Null
    if (!$DatabaseSettings) {
        if (!$PostgresBin) {
            $initdb = Get-Command initdb -ErrorAction SilentlyContinue
            if ($initdb) { $PostgresBin = Split-Path $initdb.Source }
            elseif ($IsWindows) {
                $installation = Get-ChildItem 'C:/Program Files/PostgreSQL' -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending | Select-Object -First 1
                if ($installation) { $PostgresBin = Join-Path $installation.FullName 'bin' }
            }
        }
        if ($DatabaseRuntime -eq 'Auto') { $DatabaseRuntime = if ($PostgresBin) { 'Native' } else { 'Docker' } }
        $password = [Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
        if ($DatabaseRuntime -eq 'Docker') {
            if (!(Get-Command docker -ErrorAction SilentlyContinue)) { throw 'Install PostgreSQL 18 or Docker, or supply -PostgresBin.' }
            $container = 'sengoku-local-' + [Guid]::NewGuid().ToString('N')
            $env:POSTGRES_PASSWORD = $password
            docker run --rm --detach --name $container --publish '127.0.0.1::5432' -e POSTGRES_DB=sengoku_local -e POSTGRES_USER=sengoku_local -e POSTGRES_PASSWORD postgres:18 | Out-Null
            if ($LASTEXITCODE -ne 0) { throw 'Unable to start the disposable PostgreSQL container.' }
            $mapping = docker port $container 5432/tcp
            $port = [int]($mapping.Split(':')[-1])
            $ready = $false
            for ($i=0; $i -lt 30; $i++) {
                docker exec $container pg_isready -U sengoku_local -d sengoku_local 2>&1 | Out-Null
                if ($LASTEXITCODE -eq 0) { $ready=$true; break }
                Start-Sleep -Seconds 1
            }
            if (!$ready) { throw 'Local PostgreSQL did not become ready.' }
        }
        else {
            if (!$PostgresBin -or !(Test-Path (Join-Path $PostgresBin 'initdb.exe')) -and !(Test-Path (Join-Path $PostgresBin 'initdb'))) { throw 'PostgreSQL server tools are required.' }
            $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
            $listener.Start(); $port = $listener.LocalEndpoint.Port; $listener.Stop()
            $cluster = Join-Path $run 'postgres'
            $passwordFile = Join-Path $run 'postgres-password.txt'
            [IO.File]::WriteAllText($passwordFile, $password)
            & (Join-Path $PostgresBin 'initdb') -D $cluster -U sengoku_local --auth=scram-sha-256 --pwfile=$passwordFile --encoding=UTF8 --locale=C --no-instructions *> (Join-Path $run 'initdb.log')
            if ($LASTEXITCODE -ne 0) { throw "initdb failed. Inspect $run/initdb.log" }
            $ownedCluster = $cluster
            Invoke-PgControl @('-D',$cluster,'-l',(Join-Path $run 'postgres.log'),'-o',"-h 127.0.0.1 -p $port",'-w','start') (Join-Path $run 'postgres.log')
            $env:PGPASSWORD = $password
            & (Join-Path $PostgresBin 'createdb') -h 127.0.0.1 -p $port -U sengoku_local sengoku_local
            if ($LASTEXITCODE -ne 0) { throw 'Creating the local database failed.' }
        }
        $DatabaseSettings = Join-Path $run 'settings.json'
        @{ConnectionStrings=@{AlexandriaConnectionString="Host=127.0.0.1;Port=$port;Database=sengoku_local;Username=sengoku_local;Password=$password"}} | ConvertTo-Json | Set-Content -LiteralPath $DatabaseSettings
    }
    $DatabaseSettings = (Resolve-Path -LiteralPath $DatabaseSettings).Path
    $connection = (Get-Content -LiteralPath $DatabaseSettings -Raw | ConvertFrom-Json).ConnectionStrings.AlexandriaConnectionString
    # Validate BEFORE invoking any harness that creates/drops isolated schemas.
    $parts = @{}
    foreach ($part in $connection.Split(';')) { if ($part.Contains('=')) { $k,$v=$part.Split('=',2); $parts[$k.Trim()]=$v.Trim() } }
    if ($parts.Host -notin @('127.0.0.1','localhost','::1') -or $parts.Database -notlike 'sengoku_local*') { throw 'Only a loopback sengoku_local* database is allowed.' }
    if (!$NoBuild) {
        dotnet build SengokuProvider.API/SengokuProvider.API.sln -m:1 --configuration $Configuration
        if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    }
    if (!$Serve) {
        & ./tests/Run-RegressionTests.ps1 -DatabaseSettings $DatabaseSettings -Configuration $Configuration
        if ($LASTEXITCODE -ne 0) { throw 'Regression checks failed.' }
    }
    $arguments = @('run','--project','tests/LocalEnvironment/LocalEnvironment.csproj','--configuration',$Configuration,'--no-build','--no-launch-profile','--',$DatabaseSettings)
    if ($Serve) { $arguments += '--serve' }
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Local workflow checks failed.' }
}
finally {
    if ($ownedCluster -and (Test-Path (Join-Path $ownedCluster 'postmaster.pid'))) { Invoke-PgControl @('-D',$ownedCluster,'-m','fast','-w','stop') $ownedCluster }
    if ($container) { docker stop $container | Out-Null }
    $env:PGPASSWORD=$oldPgPassword; $env:POSTGRES_PASSWORD=$oldDockerPassword
    Pop-Location
    # Keep only local, ignored files for diagnosis; no database server remains running.
}
