param(
    [Parameter(Mandatory)][string] $AppName,
    [string] $ResourceGroup = 'Alexandria',
    [Parameter(Mandatory)][ValidateSet('Provider','Dispatcher')][string] $Service,
    [string] $ExpectedRevision,
    [switch] $ConfigurationOnly,
    [int] $TimeoutSeconds = 360
)
$ErrorActionPreference = 'Stop'
$app = az webapp show -g $ResourceGroup -n $AppName -o json | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect the target App Service.' }
$hosting = az webapp config show -g $ResourceGroup -n $AppName -o json | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect hosting configuration.' }
if ($hosting.netFrameworkVersion -ne 'v8.0') { throw 'Configure the .NET 8 App Service runtime.' }
$headers = @{}
if ($Service -eq 'Dispatcher') {
    if (!$hosting.alwaysOn) { throw 'Dispatcher requires Always On.' }
    $settings = az webapp config appsettings list -g $ResourceGroup -n $AppName -o json | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect Dispatcher app settings.' }
    $values = @{}
    foreach ($setting in $settings) { $values[$setting.name.Replace('__', ':')] = $setting.value }
    foreach ($flag in @('EventFeeds:DeliveryEnabled','EventFeeds:CommsDeliverablesEnabled','Dispatcher:ServiceBusEnabled')) {
        if ($values[$flag] -ne 'true') { throw "Required setting is disabled: $flag" }
    }
    foreach ($name in @('ConnectionStrings:AlexandriaConnectionString','ServiceBusSettings:CommsDeliverablesConnection','ServiceBusSettings:AzureWebJobsServiceBus')) {
        if ([string]::IsNullOrWhiteSpace($values[$name])) { throw "Required setting is missing: $name" }
    }
    $key = $values['Dispatcher:ApiKey']
    if (!$key -or $key.Length -lt 32) { throw 'Dispatcher service authentication is not configured.' }
    # Never print settings, HTTP headers, response bodies containing errors, or secret values.
    $headers['X-Dispatcher-Key'] = $key
}
if ($ConfigurationOnly) { Write-Host "$Service configuration preflight passed."; return }
if (!$ExpectedRevision) { throw 'ExpectedRevision is required for post-deployment verification.' }
$route = if ($Service -eq 'Dispatcher') { '/internal/health/ready' } else { '/health/ready' }
$url = 'https://' + $app.defaultHostName + $route
$deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
do {
    try {
        $report = Invoke-RestMethod -Uri $url -Headers $headers -TimeoutSec 30
        if ($report.status -eq 'ready' -and $report.revision -eq $ExpectedRevision) {
            Write-Host "$Service is ready on revision $ExpectedRevision."
            return
        }
    } catch { Write-Host "$Service is not ready yet." }
    Start-Sleep -Seconds 5
} while ([DateTimeOffset]::UtcNow -lt $deadline)
throw "$Service did not become ready on the expected revision. Inspect authenticated readiness checks and worker logs; no automatic restart or reconnect was attempted."
