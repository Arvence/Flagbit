# Project definition

Flagbit is a deliberately scoped feature flag service for .NET applications. This document records the final implementation boundary and is the reference for the repository's architecture, supported behavior, and deliberate exclusions.

## Project structure

| Location | Responsibility |
| --- | --- |
| `src/Flagbit.Core` | Domain models, validation, evaluation, management services, and the focused storage abstraction. |
| `src/Flagbit.Infrastructure` | PostgreSQL stores, EF entities and mappings, normalization persistence, and migrations. |
| `src/Flagbit.Api` | HTTP contracts, endpoints, authentication, authorization, Problem Details, OpenAPI, health, and composition. |
| `src/Flagbit.Sdk` | Independent HTTP-based .NET evaluation client and caller-side variation selection. |
| `src/Flagbit.Cli` | Small management and evaluation command-line client. |
| `samples/Flagbit.SampleApi` | Real SDK integration that demonstrates live contextual feature changes. |
| `tests/Flagbit.Core.Tests` | Domain and evaluation semantics. |
| `tests/Flagbit.Api.Tests` | HTTP, persistence, migrations, authentication, and integration-boundary coverage. |
| `tests/Flagbit.Sdk.Tests` | SDK transport, compatibility, validation, and failure behavior. |
| `tests/Flagbit.Cli.Tests` | Argument handling, client failures, executable behavior, and API-backed lifecycle. |
| `scripts/start-local.ps1` | Local infrastructure preparation, migrations, API startup, readiness, and owned-process shutdown. |
| `docs/http-api.md` | HTTP contracts and executable usage guidance. |
| `.github/workflows/` | Restore, build, migration-drift, and test validation. |
| Root configuration | Solution, README, Compose, environment example, license, and local EF tool manifest. |

The final release has one implemented persistence provider: PostgreSQL. `src/Flagbit.Json` is removed. No additional runtime project is required.

## Completed feature set

- Persistent boolean feature flags with create, list, retrieve, enable, disable, evaluation-setting replacement, and delete operations.
- Predictable identifier validation and case-insensitive flag lookup.
- User targeting, deterministic percentage rollout, environment restrictions, inclusive schedules, and five string-rule operators: `Equals`, `NotEquals`, `Contains`, `StartsWith`, and `EndsWith`.
- Dependencies, chains, shared dependencies, and cycle-safe termination with explicit AND semantics across configured restrictions.
- Management mutations that preserve unrelated fields and child-data replacement that clears removed values.
- Legacy user-only GET evaluation and complete-context POST evaluation with accurate status codes, validation, Problem Details, and Development OpenAPI.
- Configured management and evaluation keys, plus generated evaluation keys with one-time disclosure, hashed storage, and persistent revocation.
- An independent .NET SDK with complete context, cancellation, explicit failures, and caller-provided variations.
- A focused CLI, a working SampleApi demonstration, migration-based PostgreSQL tests, restart-persistence coverage, native local .NET applications, Docker-backed PostgreSQL, and reproducible local setup and CI validation.

## Deliberately out of scope

- Containerizing the API, SDK, CLI, or SampleApi. They remain native .NET applications; Docker runs PostgreSQL and isolated test infrastructure.
- A dashboard, frontend, accounts, organizations, workspaces, multi-tenancy, billing, or application-specific flag partitions.
- CQRS, MediatR, an additional Application layer, generic repositories, microservices, Redis, brokers, event buses, Kubernetes, or other infrastructure that does not serve this scope.
- Streaming updates, offline or local SDK evaluation, automatic retries, caching, outage fallback, server-defined multivariate flags, experimentation, analytics, and a JSON persistence provider.
- High-scale dependency-graph optimization, distributed snapshot consistency, package publication, service deployment, and enterprise-readiness claims.

## Completion standard

The project is finished when the finalized evaluation contract is protected by meaningful regression coverage; identifiers and timestamps behave consistently through HTTP, Core, and PostgreSQL; management and child-data changes persist correctly; migrations support fresh and existing databases; the EF model matches its migration snapshot; API, SDK, CLI, and SampleApi behavior matches the documented contracts; Docker remains limited to external infrastructure and isolated tests; and a clean checkout passes the documented build, test, migration, persistence, end-to-end, dependency, and CI checks without an unresolved release-blocking defect.

See the [flow map](flow-map.md) and [request map](request-map.md) for the component and request paths that implement this definition.
