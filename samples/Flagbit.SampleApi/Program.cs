using System.Text.Json;
using Flagbit.Sdk;

var builder = WebApplication.CreateBuilder(args);

var apiUrl = builder.Configuration["Flagbit:ApiUrl"] ?? throw new InvalidOperationException("Flagbit:ApiUrl is required.");
var apiKey = builder.Configuration["Flagbit:ApiKey"];

if (string.IsNullOrWhiteSpace(apiKey))
{
    throw new InvalidOperationException("Set Flagbit__ApiKey to an evaluation API key before starting the sample.");
}

if (apiKey.Any(character => character < ' ' || character > '~'))
{
    throw new InvalidOperationException("Flagbit__ApiKey must contain only printable ASCII characters suitable for an HTTP header.");
}

if (!Uri.TryCreate(apiUrl.TrimEnd('/') + "/", UriKind.Absolute, out var baseAddress)
    || (baseAddress.Scheme != Uri.UriSchemeHttp && baseAddress.Scheme != Uri.UriSchemeHttps)
    || string.IsNullOrEmpty(baseAddress.Host)
    || !string.IsNullOrEmpty(baseAddress.UserInfo)
    || !string.IsNullOrEmpty(baseAddress.Query)
    || !string.IsNullOrEmpty(baseAddress.Fragment))
{
    throw new InvalidOperationException("Flagbit__ApiUrl must be an absolute HTTP(S) URL without credentials, a query, or a fragment.");
}

builder.Services.AddHttpClient<FlagbitClient>(client =>
{
    client.BaseAddress = baseAddress;
    client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
});

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new { name = "Flagbit Sample API", checkoutUrl = "/checkout" }));

app.MapGet("/checkout", async (FlagbitClient flagbit, string? userId, string? environment, string? plan, CancellationToken cancellationToken) =>
{
    const string flagKey = "sample-new-checkout";
    var attributes = plan is null ? null : new Dictionary<string, string> { ["plan"] = plan };
    var context = new FeatureFlagEvaluationContext(userId, environment, attributes);

    try
    {
        var isEnabled = await flagbit.EvaluateAsync(flagKey, context, cancellationToken);
        return Results.Ok(new
        {
            flagKey,
            isEnabled,
            checkout = isEnabled ? "modern" : "classic",
            message = isEnabled ? "Modern checkout" : "Classic checkout"
        });
    }
    catch (Exception exception) when (exception is HttpRequestException or JsonException)
    {
        return Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "Feature flag evaluation failed", detail: "Check Flagbit availability and the sample's evaluation key.");
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        return Results.Problem(statusCode: StatusCodes.Status504GatewayTimeout, title: "Feature flag evaluation timed out");
    }
});

app.Run();

public partial class Program
{
}
