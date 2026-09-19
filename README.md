# Flagbit

> [!IMPORTANT]
> Flagbit is currently in early development and is not production-ready. Its APIs, data model, and feature set may change as the project evolves.

Flagbit is a lightweight feature flag management platform for .NET applications. It is being built to let applications change feature availability without rebuilding or redeploying, while supporting centralized flag management, runtime evaluation, user targeting, percentage rollouts, environments, rules, schedules, and flag dependencies.

## Technology

Flagbit is built with C# and .NET 10. It uses ASP.NET Core for its HTTP API, Entity Framework Core for persistence, and provides a reusable .NET SDK. Tests are written with xUnit, with Testcontainers used for PostgreSQL integration coverage.

## Local server scope

The local server supports flag creation, listing, retrieval, evaluation-setting updates, deletion, enable/disable operations, and evaluation with user, environment, and attribute context. PostgreSQL stores definitions and settings across API restarts. HTTP and CLI access work without the SDK; advanced settings are managed over HTTP.

API keys are required in the current version. The SDK and SampleApi are optional clients. A dashboard, user accounts, multiple projects, Redis, and real-time updates are outside the current local server scope. `Flagbit.Json` is a placeholder project; a JSON provider is not implemented.

See the [HTTP API guide](docs/http-api.md) for endpoint contracts, an SDK-free PowerShell walkthrough, evaluation behavior, OpenAPI access, and Problem Details responses.

## PostgreSQL

PostgreSQL is used to persist feature flag definitions, enabled states, targeting and rollout settings, environments, evaluation rules, schedules, and dependencies. EF Core and Npgsql provide database access and migration support, while Docker is used for the local PostgreSQL environment during development.

## Local startup

Install the .NET 10 SDK and Docker Desktop with Linux containers, and start Docker Desktop. From the repository root, copy `.env.example` to `.env` if you do not already have one, then set its PostgreSQL credentials. Existing PostgreSQL volumes retain their original credentials; changing `.env` does not change the database password.

Set two different API keys in your PowerShell terminal and run the startup script:

```powershell
$env:ApiKeys__ManagementKey = "<your-management-api-key>"
$env:ApiKeys__EvaluationKey = "<your-evaluation-api-key>"
.\scripts\start-local.ps1
```

The script starts PostgreSQL, waits for its Compose health check, restores the local EF tool, builds the API, applies migrations, and starts the API in Development. It reports readiness only after `/health` returns `200`. Migration or startup failures stop the sequence. Database settings come from the resolved Compose configuration, so a separate development connection-string edit is unnecessary.

The default API address is `http://localhost:5070`. Use `.\scripts\start-local.ps1 -Port 5071` to select another port. API output is written to `artifacts/local/api-<port>.log` and `api-<port>.error.log`. Keep the terminal open; Ctrl+C stops the API while leaving PostgreSQL and its persistent data available. You can stop PostgreSQL separately with `docker compose stop postgres`.

In a second terminal, configure the CLI and try the management flow:

```powershell
$env:FLAGBIT_API_URL = "http://localhost:5070"
$env:FLAGBIT_API_KEY = "<your-management-api-key>"
dotnet run --project .\src\Flagbit.Cli -- create local-checkout
dotnet run --project .\src\Flagbit.Cli -- get local-checkout
dotnet run --project .\src\Flagbit.Cli -- enable local-checkout
dotnet run --project .\src\Flagbit.Cli -- evaluate local-checkout --user user-123 --environment production --attribute plan=enterprise
dotnet run --project .\src\Flagbit.Cli -- delete local-checkout
```

The CLI supports repeated `--attribute key=value` options. Configure advanced flag settings through the [HTTP API guide](docs/http-api.md). Evaluation can also use an evaluation key. The script reads API keys from the terminal environment; it does not load them from `.env`.

## API keys

Every `/api/flags` and `/api/keys` request requires an `X-Api-Key` header. Configure two different keys before starting the API:

| API environment variable | Access |
| --- | --- |
| `ApiKeys__ManagementKey` | All flag management, evaluation, and API key management endpoints |
| `ApiKeys__EvaluationKey` | Only `GET /api/flags/{key}/enabled` and `POST /api/flags/{key}/evaluate` |

The API refuses to start if either key is blank or both keys are identical. Missing, invalid, or multiple keys return `401 Unauthorized`; an evaluation key used for management returns `403 Forbidden`. `/`, `/health`, and the Development-only OpenAPI document remain accessible without a key.

For local development, set two different random secret values in PowerShell and start the API after configuring PostgreSQL. Replace the placeholders with your own keys:

```powershell
$env:ApiKeys__ManagementKey = "<your-management-api-key>"
$env:ApiKeys__EvaluationKey = "<your-evaluation-api-key>"
dotnet tool restore
dotnet ef database update --project .\src\Flagbit.Infrastructure --startup-project .\src\Flagbit.Api -- --environment Development
dotnet run --project .\src\Flagbit.Api
```

In a second terminal, set `FLAGBIT_API_KEY` to the same management or evaluation key configured for the running API:

```powershell
$env:FLAGBIT_API_KEY = "<your-management-api-key>"
dotnet run --project .\src\Flagbit.Cli -- create new-checkout
dotnet run --project .\src\Flagbit.Cli -- enable new-checkout

$env:FLAGBIT_API_KEY = "<your-evaluation-api-key>"
dotnet run --project .\src\Flagbit.Cli -- evaluate new-checkout
```

The [HTTP examples](src/Flagbit.Api/Flagbit.Api.http) read the API keys from the HTTP editor's process environment using [`$processEnv`](https://learn.microsoft.com/en-us/aspnet/core/test/http-files?view=aspnetcore-10.0#environment-variables). Launch the editor with those environment variables available. For SDK usage, add `X-Api-Key` to the `HttpClient.DefaultRequestHeaders` before constructing `FlagbitClient`.

Keep real keys outside source control and use HTTPS when sending them beyond localhost. Changing either configured key requires an API restart.

## Application evaluation keys

The management key can create separate evaluation keys for applications. Apply the database migration shown above before using these endpoints.

| Endpoint | Result |
| --- | --- |
| `POST /api/keys` with `{"name":"sample-app"}` | `201` with `id`, `name`, `createdAt`, and the generated `key` |
| `GET /api/keys` | Active generated keys with only `id`, `name`, and `createdAt` |
| `DELETE /api/keys/{id}` | `204` after revocation; `404` for an unknown or already revoked ID |

Names are trimmed and must contain 1–100 characters. Each secret contains 32 cryptographically random bytes and is returned only when created, with `Cache-Control: no-store`. PostgreSQL stores its SHA-256 hash. Generated keys can call the two evaluation endpoints; management operations return `403`.

For example, with the API running and `new-checkout` created:

```powershell
$managementHeaders = @{ "X-Api-Key" = "<your-management-api-key>" }
$applicationKey = Invoke-RestMethod -Method Post -Uri "http://localhost:5070/api/keys" -Headers $managementHeaders -ContentType "application/json" -Body '{"name":"sample-app"}'
$env:FLAGBIT_API_KEY = $applicationKey.key
dotnet run --project .\src\Flagbit.Cli -- evaluate new-checkout

Invoke-RestMethod -Method Get -Uri "http://localhost:5070/api/keys" -Headers $managementHeaders
Invoke-RestMethod -Method Delete -Uri "http://localhost:5070/api/keys/$($applicationKey.id)" -Headers $managementHeaders
```

Revocation deletes the stored key. The next request using it returns `401`, including on other API instances sharing the database; other keys continue to work. Generated keys persist across restarts and are checked in PostgreSQL on every request, without caching. The configured management and evaluation keys remain supported and are not included in the generated-key list.
