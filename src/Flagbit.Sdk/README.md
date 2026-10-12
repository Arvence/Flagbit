# Flagbit SDK

The Flagbit SDK is an optional .NET HTTP client for evaluating flags through a running Flagbit API. The server owns flag definitions and evaluation; the SDK does not persist definitions, cache results, retry failed requests, or provide an outage fallback.

Install the package from your configured NuGet source:

```powershell
dotnet add package Flagbit.Sdk
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

Use the cancellation-token overloads when the caller has a request deadline. HTTP errors and malformed responses are surfaced to the caller so the application can choose its own failure policy.
