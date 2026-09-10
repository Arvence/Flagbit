# Flagbit Sample API

A small ASP.NET Core application that uses `Flagbit.Sdk` to switch between classic and modern checkout behavior without restarting the application. The sample returns JSON; it does not render a checkout page.

Each request to `/checkout` asks the Flagbit API to evaluate `sample-new-checkout`. An enabled flag selects `modern`; a disabled or missing flag selects `classic`.

## Prerequisites

- .NET 10 SDK.
- A running Flagbit API with PostgreSQL and current migrations. Follow the [main README](../../README.md) for API configuration and startup.
- The management API key configured for that API.

Run all commands below in PowerShell from the repository root. Keep the Flagbit API running in its own terminal. The default addresses are `http://localhost:5070` for Flagbit and `http://localhost:5131` for this sample.

## Create the flag

In a management terminal, replace the key placeholder with your configured management key:

```powershell
$env:FLAGBIT_API_URL = "http://localhost:5070"
$env:FLAGBIT_API_KEY = "<your-management-api-key>"

dotnet run --project .\src\Flagbit.Cli -- create sample-new-checkout
```

New flags are disabled by default. If this flag already exists, reuse it and skip creation. Use a flag without targeting or other evaluation conditions for the initial on/off example.

## Start the sample

In another terminal, create an application evaluation key and pass it to the sample through an environment variable:

```powershell
$apiUrl = "http://localhost:5070"
$managementHeaders = @{ "X-Api-Key" = "<your-management-api-key>" }
$applicationKey = Invoke-RestMethod -Method Post -Uri "$apiUrl/api/keys" -Headers $managementHeaders -ContentType "application/json" -Body '{"name":"Flagbit.SampleApi"}'

$env:Flagbit__ApiUrl = $apiUrl
$env:Flagbit__ApiKey = $applicationKey.key

dotnet run --project .\samples\Flagbit.SampleApi --launch-profile http
```

Keep this terminal running. `Flagbit__ApiUrl` overrides the address in `appsettings.json`. The sample requires `Flagbit__ApiKey` at startup; use the generated evaluation key, not the management key. Keep secrets outside source control.

## Test on/off behavior

Return to the management terminal:

```powershell
dotnet run --project .\src\Flagbit.Cli -- disable sample-new-checkout
Invoke-RestMethod http://localhost:5131/checkout

dotnet run --project .\src\Flagbit.Cli -- enable sample-new-checkout
Invoke-RestMethod http://localhost:5131/checkout

dotnet run --project .\src\Flagbit.Cli -- disable sample-new-checkout
Invoke-RestMethod http://localhost:5131/checkout
```

| Flag state | `isEnabled` | `checkout` | `message` |
| --- | --- | --- | --- |
| Disabled | `false` | `classic` | Classic checkout |
| Enabled | `true` | `modern` | Modern checkout |
| Disabled again | `false` | `classic` | Classic checkout |

The sample stays running throughout. You can also open [the checkout endpoint](http://localhost:5131/checkout) in a browser and refresh after changing the flag.

## Test user targeting

In the management terminal, restrict the flag to `demo-user` and enable it:

```powershell
$headers = @{ "X-Api-Key" = $env:FLAGBIT_API_KEY }
$evaluationUrl = "$env:FLAGBIT_API_URL/api/flags/sample-new-checkout/evaluation"

Invoke-RestMethod -Method Put -Uri $evaluationUrl -Headers $headers -ContentType "application/json" -Body '{"targetedUserIds":["demo-user"]}'
dotnet run --project .\src\Flagbit.Cli -- enable sample-new-checkout

Invoke-RestMethod "http://localhost:5131/checkout?userId=demo-user"
Invoke-RestMethod "http://localhost:5131/checkout?userId=guest"
```

`demo-user` receives `modern`; `guest` receives `classic`. A request without `userId` also receives `classic` while this targeting restriction is active.

The evaluation update replaces the flag's evaluation settings. To clear those settings and return the sample flag to its disabled state:

```powershell
Invoke-RestMethod -Method Put -Uri $evaluationUrl -Headers $headers -ContentType "application/json" -Body '{}'
dotnet run --project .\src\Flagbit.Cli -- disable sample-new-checkout
```

## Stop and clean up

Press `Ctrl+C` in the sample terminal. To revoke the application key created in that terminal:

```powershell
Invoke-RestMethod -Method Delete -Uri "$apiUrl/api/keys/$($applicationKey.id)" -Headers $managementHeaders
```

If you created the flag only for this example, delete it from the management terminal:

```powershell
dotnet run --project .\src\Flagbit.Cli -- delete sample-new-checkout
```

The sample makes a network request for each evaluation. Connection failures or invalid/revoked credentials produce a request error; the sample does not provide an outage fallback.
