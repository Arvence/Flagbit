using Flagbit.Api;
using Flagbit.Api.ErrorHandling;
using Flagbit.Core.Abstractions;
using Flagbit.Core.Services;
using Flagbit.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IFeatureFlagStore, FeatureFlagStore>();
builder.Services.AddSingleton<FeatureFlagManager>();
builder.Services.AddSingleton<FeatureFlagEvaluator>();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

var app = builder.Build();

app.UseExceptionHandler();

app.MapGet("/", () => Results.Ok(new { name = "Flagbit API", status = "running" }));
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapFeatureFlagEndpoints();

app.Run();

public partial class Program
{
}
