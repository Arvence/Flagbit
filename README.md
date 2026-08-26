# Flagbit

> [!IMPORTANT]
> Flagbit is currently in early development and is not production-ready. Its APIs, data model, and feature set may change as the project evolves.

Flagbit is a lightweight feature flag management platform for .NET applications. It is being built to let applications change feature availability without rebuilding or redeploying, while supporting centralized flag management, runtime evaluation, user targeting, percentage rollouts, environments, rules, schedules, and flag dependencies.

## Technology

Flagbit is built with C# and .NET 10. It uses ASP.NET Core for its HTTP API, Entity Framework Core for persistence, and provides a reusable .NET SDK alongside a local JSON provider. Tests are written with xUnit, with Testcontainers used for PostgreSQL integration coverage.

## PostgreSQL

PostgreSQL is used to persist feature flag definitions, enabled states, targeting and rollout settings, environments, evaluation rules, schedules, and dependencies. EF Core and Npgsql provide database access and migration support, while Docker is used for the local PostgreSQL environment during development.
