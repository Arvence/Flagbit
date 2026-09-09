using Flagbit.Api;
using Flagbit.Api.Authentication;
using Flagbit.Api.ErrorHandling;
using Flagbit.Core.Abstractions;
using Flagbit.Core.Services;
using Flagbit.Infrastructure;
using Flagbit.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<FlagbitDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("PostgreSQL")
        ?? throw new InvalidOperationException("The PostgreSQL connection string is not configured.")));
builder.Services.AddScoped<IFeatureFlagStore, FeatureFlagStore>();
builder.Services.AddScoped<FeatureFlagManager>();
builder.Services.AddScoped<FeatureFlagEvaluator>();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOptions<ApiKeyOptions>()
    .BindConfiguration(ApiKeyOptions.SectionName)
    .Validate(options => !string.IsNullOrWhiteSpace(options.ManagementKey), "ApiKeys:ManagementKey is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.EvaluationKey), "ApiKeys:EvaluationKey is required.")
    .Validate(options => !string.Equals(options.ManagementKey, options.EvaluationKey, StringComparison.Ordinal), "Management and evaluation API keys must be different.")
    .ValidateOnStart();
builder.Services.AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, null);
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(ApiKeyOptions.ManagementPolicy, policy => policy.RequireAuthenticatedUser().RequireRole(ApiKeyOptions.ManagementPolicy));
    options.AddPolicy(ApiKeyOptions.EvaluationPolicy, policy => policy.RequireAuthenticatedUser().RequireRole(ApiKeyOptions.ManagementPolicy, ApiKeyOptions.EvaluationPolicy));
});
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks()
    .AddDbContextCheck<FlagbitDbContext>("postgresql");

var app = builder.Build();

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/", () => Results.Ok(new { name = "Flagbit API", status = "running" }));
app.MapHealthChecks("/health");
app.MapFeatureFlagEndpoints();

app.Run();

public partial class Program
{
}
