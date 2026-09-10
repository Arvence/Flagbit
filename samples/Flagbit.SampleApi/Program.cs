using Flagbit.Sdk;

var builder = WebApplication.CreateBuilder(args);

var apiUrl = builder.Configuration["Flagbit:ApiUrl"] ?? throw new InvalidOperationException("Flagbit:ApiUrl is required.");
var apiKey = builder.Configuration["Flagbit:ApiKey"];

if (string.IsNullOrWhiteSpace(apiKey))
{
    throw new InvalidOperationException("Set Flagbit__ApiKey to an evaluation API key before starting the sample.");
}

builder.Services.AddHttpClient<FlagbitClient>(client =>
{
    client.BaseAddress = new Uri(apiUrl);
    client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
});

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new { name = "Flagbit Sample API", checkoutUrl = "/checkout" }));

app.MapGet("/checkout", async (FlagbitClient flagbit, string? userId, CancellationToken cancellationToken) =>
{
    const string flagKey = "sample-new-checkout";
    var isEnabled = await flagbit.IsEnabledAsync(flagKey, userId, cancellationToken);

    return Results.Ok(new
    {
        flagKey,
        isEnabled,
        checkout = isEnabled ? "modern" : "classic",
        message = isEnabled ? "Modern checkout" : "Classic checkout"
    });
});

app.Run();
