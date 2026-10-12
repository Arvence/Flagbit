# Flow map

These maps describe the implemented ownership boundaries. The Mermaid diagrams use a linear curve, so every arrow is straight and points toward the next component or startup step.

## Runtime component flow

```mermaid
%%{init: {"flowchart": {"curve": "linear"}} }%%
flowchart TB
    Application[.NET application] --> Sdk[Flagbit.Sdk]
    SampleApi[Flagbit.SampleApi] --> Sdk
    Cli[Flagbit.Cli] --> Api[Flagbit.Api]
    Sdk --> Api
    Api --> Authentication[API key authentication and authorization]
    Authentication --> Endpoints[HTTP endpoints and Problem Details]
    Endpoints --> Core[Flagbit.Core services]
    Core --> Store[Focused storage abstractions]
    Store --> Infrastructure[Flagbit.Infrastructure stores and mappings]
    Infrastructure --> PostgreSQL[(PostgreSQL)]
```

`Flagbit.Api` owns HTTP contracts, authentication, authorization, OpenAPI, health, and service composition. `Flagbit.Core` owns identifier validation, management behavior, and boolean evaluation semantics. `Flagbit.Infrastructure` owns PostgreSQL entities, mappings, normalization, and EF Core migrations.

The SDK is an independent HTTP client. It sends one request per evaluation, accepts a caller-owned `HttpClient` and context, and leaves variation selection and failure policy to the caller. The CLI is also an HTTP client. The SampleApi uses the SDK for one contextual evaluation per checkout request. None of these clients performs local flag evaluation, caching, retries, or outage fallback.

## Local startup flow

```mermaid
%%{init: {"flowchart": {"curve": "linear"}} }%%
flowchart TB
    Developer[Developer terminal] --> StartScript[scripts/start-local.ps1]
    StartScript --> Compose[Docker Compose starts PostgreSQL]
    Compose --> DatabaseReady[PostgreSQL health check]
    DatabaseReady --> EfTool[Restore local dotnet-ef tool]
    EfTool --> BuildApi[Build Flagbit.Api]
    BuildApi --> Migrations[Apply EF Core migrations]
    Migrations --> NativeApi[Start native Flagbit.Api process]
    NativeApi --> HealthCheck[GET /health returns 200]
```

Docker is used only for PostgreSQL. The API, CLI, SDK consumer, and SampleApi remain native .NET processes. The startup script resolves the Compose PostgreSQL connection, applies migrations before API readiness, writes API logs under `artifacts/local`, and stops the API process it owns when its terminal closes. The PostgreSQL container and its volume remain available until they are stopped separately.

## Evaluation decision flow

```mermaid
%%{init: {"flowchart": {"curve": "linear"}} }%%
flowchart TB
    Request[Authenticated evaluation request] --> LoadFlag[Load persisted flag definition]
    LoadFlag --> Enabled{Flag enabled?}
    Enabled --> Targeting[Check user targeting]
    Targeting --> Rollout[Check deterministic percentage rollout]
    Rollout --> Environment[Check environment]
    Environment --> Rules[Check all configured string rules]
    Rules --> Schedule[Check inclusive schedule]
    Schedule --> Dependencies[Check dependencies with cycle protection]
    Dependencies --> Result[Return boolean evaluation result]
```

Configured restrictions use AND semantics. A disabled or missing flag, an unmet restriction, or a dependency cycle returns `false`. Matching supports case-insensitive identifiers, user targeting, deterministic rollout buckets, environments, five string-rule operators, inclusive schedules, dependency chains, shared dependencies, and safe cycle handling.
