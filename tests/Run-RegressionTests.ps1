param(
    [string] $DatabaseSettings,
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$previousBracketConnection = $env:BRACKET_TEST_POSTGRES
$previousUserConnection = $env:USER_TEST_POSTGRES
$failedSuites = @()
Push-Location $repositoryRoot
try {
    if ($DatabaseSettings) {
        $DatabaseSettings = (Resolve-Path -LiteralPath $DatabaseSettings).Path
        $settings = Get-Content -LiteralPath $DatabaseSettings -Raw | ConvertFrom-Json
        $connection = $settings.ConnectionStrings.AlexandriaConnectionString
        if ([string]::IsNullOrWhiteSpace($connection)) {
            throw 'Database settings must contain ConnectionStrings.AlexandriaConnectionString.'
        }
        # Each harness creates and removes its own uniquely named schema.
        $env:BRACKET_TEST_POSTGRES = $connection
        $env:USER_TEST_POSTGRES = $connection
    }
    else {
        Write-Host 'Database checks require -DatabaseSettings; running the available non-database checks.'
    }

    foreach ($suite in @('BracketWorkflow', 'BracketPathQuery', 'UserOnboarding', 'AccountAuthentication', 'EventFeeds')) {
        Write-Host "Running $suite regression checks"
        $arguments = @('run', '--project', "tests/$suite.Tests/$suite.Tests.csproj", '--configuration', $Configuration, '--no-build')
        if ($DatabaseSettings -and $suite -in @('AccountAuthentication', 'EventFeeds')) {
            $arguments += @('--', $DatabaseSettings)
        }
        & dotnet @arguments
        if ($LASTEXITCODE -ne 0) { $failedSuites += $suite }
    }
    if ($failedSuites.Count -gt 0) {
        throw "Regression suites failed: $($failedSuites -join ', ')"
    }
    Write-Host 'All five regression harnesses passed.'
}
finally {
    $env:BRACKET_TEST_POSTGRES = $previousBracketConnection
    $env:USER_TEST_POSTGRES = $previousUserConnection
    Pop-Location
}
