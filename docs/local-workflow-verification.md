# Local workflow verification

TODO 7 verification notes, recorded on October 10, 2026. These commands and observed results are input for the final documentation pass.

## Verified environment

- Windows 11 Pro; Windows PowerShell 5.1.26100.9444.
- .NET SDK 10.0.302, Docker Engine 29.7.2, Docker Compose 5.4.0, PostgreSQL 16.
- API, CLI, and SampleApi ran as native .NET applications. Only PostgreSQL ran in Docker.
- The verification used a separate Compose project, container, volume, database, and ports. The normal `.env`, PostgreSQL container, and persistent volume were not changed.

PowerShell 7, Linux, macOS, and an interactive IDE debugger session were not exercised. Both existing `http` launch profiles were executed with `dotnet run`; the IDE procedure below uses the same environment and profiles.

## Isolated database and startup

Run from the repository root. Ports 58267, 58268, and 58269 were used for PostgreSQL, the API, and SampleApi respectively; choose other unused ports if necessary.

```powershell
$verificationRoot = Join-Path $PWD "artifacts/local-verification"
New-Item -ItemType Directory -Path $verificationRoot -Force | Out-Null
$envFile = Join-Path $verificationRoot "postgres.env"
$databasePassword = [Guid]::NewGuid().ToString("N") + ";local"
@("POSTGRES_DB=flagbit_todo7", "POSTGRES_USER=flagbit", "POSTGRES_PASSWORD=$databasePassword") | Set-Content -LiteralPath $envFile

$env:COMPOSE_PROJECT_NAME = "flagbit-verification-" + [Guid]::NewGuid().ToString("N").Substring(0, 8)
$overrideFile = Join-Path $verificationRoot "compose.override.yml"
@"
services:
  postgres:
    container_name: $($env:COMPOSE_PROJECT_NAME)-postgres
    ports: !override
      - "127.0.0.1:58267:5432"
"@ | Set-Content -LiteralPath $overrideFile
$env:COMPOSE_FILE = (Join-Path $PWD "docker-compose.yml") + ";" + $overrideFile

# Let the selected environment file supply the database credentials.
Remove-Item Env:POSTGRES_DB, Env:POSTGRES_USER, Env:POSTGRES_PASSWORD -ErrorAction SilentlyContinue
$env:ApiKeys__ManagementKey = "management-" + [Guid]::NewGuid().ToString("N")
$env:ApiKeys__EvaluationKey = "evaluation-" + [Guid]::NewGuid().ToString("N")
```

Keep the keys and selected Compose environment available in the terminals used below. Do not regenerate them during a restart. The following command prepares the fresh database and starts the API:

```powershell
.\scripts\start-local.ps1 -Port 58268 -EnvFile $envFile
```

The observed sequence was Compose health success, EF tool restore, API build, all three migrations, API startup, then `Flagbit is ready at http://localhost:58268. Health check passed.` The database password included a semicolon, verifying connection-string quoting. Applied migrations were:

1. `20260826093617_InitialPostgreSql`
2. `20260909112642_AddEvaluationApiKeys`
3. `20261007161637_UseApplicationIdentifierNormalization`

## Preparation and IDE launch

For a separate native or IDE launch, stop the script-owned API first and run this in a terminal containing the same keys and Compose settings:

```powershell
.\scripts\start-local.ps1 -Port 58268 -EnvFile $envFile -PrepareOnly
dotnet run --project .\src\Flagbit.Api --no-build --launch-profile http -- --urls http://localhost:58268
```

Preparation-only mode starts/waits for PostgreSQL, builds, and applies migrations. It starts no API process and intentionally retains the resolved `ConnectionStrings__PostgreSQL`, both development environment variables, and `ASPNETCORE_URLS` in that terminal. API keys remain the values supplied by the caller. Closing the terminal discards these process-scoped settings.

For standard IDE ports, use `-PrepareOnly` without `-Port`, set `Flagbit__ApiUrl` to `http://localhost:5070`, and set `Flagbit__ApiKey` to an evaluation key. Launch a **new IDE process from that prepared terminal**, open `Flagbit.sln`, and select the API and SampleApi projects with their existing `http` profiles. Those profiles bind to 5070 and 5131. An IDE process already running before preparation will not inherit the new environment. With custom ports, supply the corresponding `--urls` application arguments and sample API URL explicitly.

The checked-in API development configuration contains logging settings, with no separate database credentials. The preparation mode and ordinary startup share the same Compose resolution and migration logic.

## CLI and sample integration

In a management terminal with the same management key:

```powershell
$apiUrl = "http://localhost:58268"
$sampleUrl = "http://localhost:58269"
$headers = @{ "X-Api-Key" = $env:ApiKeys__ManagementKey }
$env:FLAGBIT_API_URL = $apiUrl
$env:FLAGBIT_API_KEY = $env:ApiKeys__ManagementKey

dotnet run --project .\src\Flagbit.Cli -- list
dotnet run --project .\src\Flagbit.Cli -- create cli-verification
dotnet run --project .\src\Flagbit.Cli -- get cli-verification
dotnet run --project .\src\Flagbit.Cli -- enable cli-verification
dotnet run --project .\src\Flagbit.Cli -- evaluate cli-verification
dotnet run --project .\src\Flagbit.Cli -- disable cli-verification
dotnet run --project .\src\Flagbit.Cli -- delete cli-verification

dotnet run --project .\src\Flagbit.Cli -- create sample-new-checkout
$applicationKey = Invoke-RestMethod -Method Post -Uri "$apiUrl/api/keys" -Headers $headers -ContentType "application/json" -Body '{"name":"SampleApi verification"}'
```

Keep `$applicationKey` in the management terminal for evaluation and revocation. In a separate sample terminal, supply its generated key value:

```powershell
$env:Flagbit__ApiUrl = "http://localhost:58268"
$env:Flagbit__ApiKey = "<applicationKey.key from the management terminal>"
dotnet run --project .\samples\Flagbit.SampleApi --launch-profile http -- --urls http://localhost:58269
```

Every CLI command above must exit with `0`. The temporary flag starts disabled, becomes enabled, then is disabled and deleted. Keep SampleApi running while issuing the following requests from the management terminal:

```powershell
Invoke-RestMethod "$sampleUrl/checkout" # classic
Invoke-RestMethod -Method Put -Uri "$apiUrl/api/flags/sample-new-checkout/enable" -Headers $headers
Invoke-RestMethod "$sampleUrl/checkout" # modern
Invoke-RestMethod -Method Put -Uri "$apiUrl/api/flags/sample-new-checkout/disable" -Headers $headers
Invoke-RestMethod "$sampleUrl/checkout" # classic
Invoke-RestMethod -Method Put -Uri "$apiUrl/api/flags/sample-new-checkout/enable" -Headers $headers

$evaluationUrl = "$apiUrl/api/flags/sample-new-checkout/evaluation"
$settings = '{"targetedUserIds":["demo-user"],"environments":["production"],"rules":[{"attribute":"plan","operator":"Equals","value":"enterprise"}]}'
Invoke-RestMethod -Method Put -Uri $evaluationUrl -Headers $headers -ContentType "application/json" -Body $settings
Invoke-RestMethod "$sampleUrl/checkout?userId=demo-user&environment=production&plan=enterprise" # modern
Invoke-RestMethod "$sampleUrl/checkout?userId=guest&environment=production&plan=enterprise" # classic
Invoke-RestMethod "$sampleUrl/checkout?userId=demo-user&environment=staging&plan=enterprise" # classic
Invoke-RestMethod "$sampleUrl/checkout?userId=demo-user&environment=production&plan=free" # classic

$context = '{"userId":"demo-user","environment":"production","attributes":{"plan":"enterprise"}}'
Invoke-RestMethod -Method Post -Uri "$apiUrl/api/flags/sample-new-checkout/evaluate" -Headers @{ "X-Api-Key" = $applicationKey.key } -ContentType "application/json" -Body $context
dotnet run --project .\src\Flagbit.Cli -- evaluate sample-new-checkout --user demo-user --environment production --attribute plan=enterprise
```

The raw HTTP response has `isEnabled: true`; the CLI prints `sample-new-checkout is enabled.` Individual targeting, environment, and attribute restrictions were also tested separately, including omitted context, before the combined case. All changes took effect in the same SampleApi process.

## Restart, revocation, and shutdown

Press Ctrl+C in the startup-script terminal. The API process exits, the caller's prior connection string/environment/URL variables and working directory are restored, and PostgreSQL remains healthy. Sample requests return `502` while the API is unavailable. Start the API again against the same database and keys; the matching contextual request returns `modern` without restarting SampleApi.

Revoke the application's evaluation key:

```powershell
Invoke-RestMethod -Method Delete -Uri "$apiUrl/api/keys/$($applicationKey.id)" -Headers $headers
Invoke-WebRequest "$sampleUrl/checkout?userId=demo-user&environment=production&plan=enterprise"
```

The sample returns `502` Problem Details, which PowerShell reports as an error. A second API restart keeps that key revoked; direct evaluation with it returns `401`. A newly generated evaluation key allowed the sample's `http` launch profile to serve the matching request successfully. That verification key was then revoked too.

Stop the sample and API, then stop only the selected verification database:

```powershell
docker compose --env-file $envFile stop postgres
```

No owned API, SampleApi, or `dotnet run` processes should remain. The verification container and volume are retained; `stop` does not delete data. Do not use `down --volumes` against the normal development stack.

## Failure checks and automated coverage

| Setup exercised | Observed result |
| --- | --- |
| Docker or dotnet removed from a child shell's PATH | Required-tool message before startup |
| Docker Desktop stopped | Action to start Docker Desktop with Linux containers |
| Missing selected environment file | Copy `.env.example` or choose `-EnvFile` |
| Either API key missing, identical keys, or a key containing a newline | Actionable configuration error without printing the key |
| API port reserved by another listener | Port-in-use error with the `-Port` alternative |
| Wrong password in a second environment file against the existing isolated volume | PostgreSQL `28P01`, migration failure, no API start |
| Pre-existing `feature_flags` table in a separate empty test database | PostgreSQL `42P07`, migration failure, no API start |
| `Kestrel__Endpoints__Broken__Url=ftp://127.0.0.1:12345` during normal startup | Real API exits before readiness; startup reports its log paths |

All failure checks restored the caller's environment. The conflicting table was created only in `flagbit_todo7_broken` inside the isolated verification container. The main isolated database retained its flag/settings across container recreation. The invalid Kestrel setting was removed after the check.

```powershell
dotnet test .\tests\Flagbit.Api.Tests -c Release --filter FullyQualifiedName~SampleApiTests
dotnet build .\Flagbit.sln -c Release
dotnet test .\Flagbit.sln -c Release --no-build --no-restore
```

The 15 sample-boundary tests require no Docker. They cover contextual POST requests, omitted context, enabled/disabled results, unavailable service, revoked/forbidden credentials, invalid responses, timeouts, cancellation, and invalid startup configuration. Full integration tests use disposable PostgreSQL databases.

The Release build completed with zero warnings and zero errors. The final solution test run passed all 494 tests: Core 146, SDK 45, CLI 125, and API 178. The API total includes the 15 new sample tests. Authentication tests now supply their own unused connection string instead of relying on the removed development fallback.

## Change summary and remaining verification limits

- `scripts/start-local.ps1` adds shared preparation-only mode, environment-file selection, prerequisite/key validation, and reliable owned-process cleanup.
- `src/Flagbit.Api/appsettings.Development.json` removes the duplicated database configuration. `tests/Flagbit.Api.Tests/ApiKeyAuthenticationTests.cs` explicitly configures its test connection.
- `samples/Flagbit.SampleApi/Program.cs` sends user, environment, and plan context through the SDK, validates startup configuration, and returns explicit dependency errors.
- `tests/Flagbit.Api.Tests/SampleApiTests.cs` is new; the existing test project adds an aliased sample reference without adding another test project or test packages.
- `.env.example`, `README.md`, and `samples/Flagbit.SampleApi/README.md` describe preparation, configuration inheritance, contextual evaluation, restart, and revocation. This verification document is new. No tracked files were removed.

Compose remains PostgreSQL-only. Existing launch profiles, migrations, database schema, SDK contracts, and evaluation semantics are unchanged. The sample still uses an evaluation key and makes one network evaluation per request. Normal development data was preserved, and the isolated verification container was stopped with its volume retained.

The final documentation TODO should consolidate these commands and observed results. Interactive IDE breakpoint verification and checks on other operating systems or shells remain unperformed; launching both existing profiles through `dotnet run` does not establish those results.
