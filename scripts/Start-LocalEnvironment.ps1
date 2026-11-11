param([ValidateSet('Auto','Native','Docker')][string] $DatabaseRuntime = 'Auto', [string] $PostgresBin)
$ErrorActionPreference = 'Stop'
& "$PSScriptRoot/../tests/Run-LocalTests.ps1" -Serve -DatabaseRuntime $DatabaseRuntime -PostgresBin $PostgresBin
exit $LASTEXITCODE
