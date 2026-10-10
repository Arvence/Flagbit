# Flagbit Sample API

A small ASP.NET Core application that uses `Flagbit.Sdk` to switch between classic and modern checkout behavior without restarting the application. The sample returns JSON; it does not render a checkout page.

Each request to `/checkout` asks the Flagbit API to evaluate `sample-new-checkout` through the SDK's contextual POST evaluation. An enabled result selects `modern`; a disabled or missing flag, or an unmet evaluation condition, selects `classic`. Optional `userId`, `environment`, and `plan` query parameters become the user, environment, and `plan` attribute in the evaluation context.

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

In the management terminal, create an application evaluation key and retain the returned object for the later revocation check:

```powershell
$apiUrl = "http://localhost:5070"
$managementHeaders = @{ "X-Api-Key" = "<your-management-api-key>" }
$applicationKey = Invoke-RestMethod -Method Post -Uri "$apiUrl/api/keys" -Headers $managementHeaders -ContentType "application/json" -Body '{"name":"Flagbit.SampleApi"}'
```

In a separate sample terminal, set the same API address and the generated `applicationKey.key` value:

```powershell
$env:Flagbit__ApiUrl = "http://localhost:5070"
$env:Flagbit__ApiKey = "<generated-application-evaluation-key>"

dotnet run --project .\samples\Flagbit.SampleApi --launch-profile http
```

Keep this terminal running. `Flagbit__ApiUrl` overrides the address in `appsettings.json`. The sample requires `Flagbit__ApiKey` at startup; use the generated evaluation key, not the management key. Keep secrets outside source control.

For IDE use, prepare the API as described in the [local workflow notes](../../docs/local-workflow-verification.md), set `Flagbit__ApiUrl` and `Flagbit__ApiKey` in that terminal, and launch a new IDE process from it. Select the sample's existing `http` profile. The API and sample profiles use ports 5070 and 5131 respectively.

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

## Test environment and attribute context

Keep SampleApi running. Replace the evaluation settings with a combined user, environment, and plan requirement:

```powershell
Invoke-RestMethod -Method Put -Uri $evaluationUrl -Headers $headers -ContentType "application/json" -Body '{"targetedUserIds":["demo-user"],"environments":["production"],"rules":[{"attribute":"plan","operator":"Equals","value":"enterprise"}]}'
dotnet run --project .\src\Flagbit.Cli -- enable sample-new-checkout

Invoke-RestMethod "http://localhost:5131/checkout?userId=demo-user&environment=production&plan=enterprise"
Invoke-RestMethod "http://localhost:5131/checkout?userId=demo-user&environment=staging&plan=enterprise"
Invoke-RestMethod "http://localhost:5131/checkout?userId=demo-user&environment=production&plan=free"
```

The first request returns `modern`; the next two return `classic`. Omitting any required context also returns `classic`. These query values are demonstration inputs, not an application authorization mechanism.

Stop and restart only the Flagbit API using the same database and API keys. The matching sample request still returns `modern`, with the same SampleApi process and generated evaluation key. While the API is stopped, sample requests return `502` Problem Details.

In the management terminal, revoke the generated key while SampleApi remains running:

```powershell
Invoke-RestMethod -Method Delete -Uri "$apiUrl/api/keys/$($applicationKey.id)" -Headers $managementHeaders
Invoke-WebRequest "http://localhost:5131/checkout?userId=demo-user&environment=production&plan=enterprise"
```

The sample now returns `502` Problem Details. PowerShell reports that non-success status as a request error. Restarting the API does not reactivate the key. Create a new evaluation key and restart the sample with it to resume successful requests.

## Stop and clean up

Press `Ctrl+C` in the sample terminal. If you skipped the revocation scenario, revoke the application key from the management terminal:

```powershell
Invoke-RestMethod -Method Delete -Uri "$apiUrl/api/keys/$($applicationKey.id)" -Headers $managementHeaders
```

If you created the flag only for this example, delete it from the management terminal:

```powershell
dotnet run --project .\src\Flagbit.Cli -- delete sample-new-checkout
```

The sample makes a network request for each evaluation. Connection failures, invalid/revoked credentials, and malformed evaluation responses return `502` Problem Details; upstream timeouts return `504`. Caller cancellation is forwarded to the SDK. These failures never become a successful classic checkout response.
