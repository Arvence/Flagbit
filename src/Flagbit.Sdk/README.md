# Flagbit SDK

The Flagbit SDK is an optional .NET HTTP client for evaluating flags through a running Flagbit API. The server owns flag definitions and evaluation; the SDK does not persist definitions, cache results, retry failed requests, or provide an outage fallback.

Build the package from the Flagbit repository root, then install it from that local feed in a .NET 10 consumer. Replace the example feed path with the absolute path to your Flagbit checkout's `artifacts/nuget` directory.

```powershell
dotnet pack src/Flagbit.Sdk/Flagbit.Sdk.csproj -c Release -o artifacts/nuget
```

```powershell
dotnet add package Flagbit.Sdk --version 1.0.0 --source 'C:/path/to/Flagbit/artifacts/nuget'
```

Configure `HttpClient` with the Flagbit API address and an evaluation key, then provide the context needed by the flag:

```csharp
using Flagbit.Sdk;

using var http = new HttpClient { BaseAddress = new Uri("https://flags.example/") };
http.DefaultRequestHeaders.Add("X-Api-Key", "<your-evaluation-api-key>");

var flags = new FlagbitClient(http);
var context = new FeatureFlagEvaluationContext(UserId: "user-123", Environment: "production", Attributes: new Dictionary<string, string> { ["plan"] = "enterprise" });

var enabled = await flags.EvaluateAsync("new-checkout", context);
```

Use the cancellation-token overloads when the caller has a request deadline. HTTP failures, including `401` after key revocation, surface as `HttpRequestException`. Invalid evaluation JSON surfaces as `JsonException`. A valid `false` result remains distinguishable from these failures.
