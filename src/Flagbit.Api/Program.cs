using Flagbit.Api;
using Flagbit.Api.ErrorHandling;
using Flagbit.Core.Abstractions;
using Flagbit.Core.Services;
using Flagbit.Infrastructure;
using Flagbit.Infrastructure.Persistence;
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
builder.Services.AddHealthChecks()
    .AddDbContextCheck<FlagbitDbContext>("postgresql");

var app = builder.Build();

app.UseExceptionHandler();

app.MapGet("/", () => Results.Ok(new { name = "Flagbit API", status = "running" }));
app.MapHealthChecks("/health");
app.MapFeatureFlagEndpoints();

app.Run();

public partial class Program
{
}
