# Flagbit

Flagbit is a lightweight feature flag management platform built with C# and .NET. It is designed to let applications enable or disable features without rebuilding or redeploying them.

## Repository structure

- `src/Flagbit.Api` - ASP.NET Core API
- `src/Flagbit.Core` - domain and business logic
- `src/Flagbit.Infrastructure` - infrastructure and persistence
- `src/Flagbit.Sdk` - reusable .NET client SDK
- `samples/Flagbit.SampleApi` - example SDK consumer
- `tests/Flagbit.Api.Tests` - API tests
- `tests/Flagbit.Sdk.Tests` - SDK tests

## Build

The solution targets .NET 10.

```bash
dotnet build Flagbit.sln
```

## Test

```bash
dotnet test Flagbit.sln
```
