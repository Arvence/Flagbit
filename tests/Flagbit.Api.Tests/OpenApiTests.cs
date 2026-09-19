using System.Net;
using System.Text.Json;
using Flagbit.Api.Authentication;
using Flagbit.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flagbit.Api.Tests;

[Collection(PostgreSqlCollection.Name)]
public sealed class OpenApiTests
{
    private readonly PostgreSqlFixture _postgreSql;

    public OpenApiTests(PostgreSqlFixture postgreSql)
    {
        _postgreSql = postgreSql;
    }

    [Fact]
    public async Task DocumentIncludesManagementAndEvaluationEndpoints()
    {
        using var application = CreateApplication();
        using var client = application.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");
        var document = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"/api/flags\"", document);
        Assert.Contains("\"/api/flags/{key}/evaluate\"", document);
        Assert.Contains("\"/api/keys\"", document);
        Assert.Contains("\"/api/keys/{id}\"", document);

        using var json = JsonDocument.Parse(document);
        var paths = json.RootElement.GetProperty("paths");
        var createResponses = paths.GetProperty("/api/flags").GetProperty("post").GetProperty("responses");
        Assert.Equal("#/components/schemas/FeatureFlagResponse", createResponses.GetProperty("201").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());
        Assert.True(createResponses.GetProperty("409").GetProperty("content").TryGetProperty("application/problem+json", out _));
        Assert.True(createResponses.TryGetProperty("401", out _));
        Assert.True(createResponses.TryGetProperty("403", out _));

        var evaluationResponses = paths.GetProperty("/api/flags/{key}/evaluate").GetProperty("post").GetProperty("responses");
        Assert.Equal("#/components/schemas/FeatureFlagEvaluationResponse", evaluationResponses.GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());

        var schemas = json.RootElement.GetProperty("components").GetProperty("schemas");
        var flagProperties = schemas.GetProperty("FeatureFlagResponse").GetProperty("properties");
        foreach (var property in new[] { "key", "isEnabled", "targetedUserIds", "rolloutPercentage", "environments", "rules", "startsAt", "endsAt", "dependencyKeys" })
        {
            Assert.True(flagProperties.TryGetProperty(property, out _), $"The flag response schema must include {property}.");
        }

        var evaluationProperties = schemas.GetProperty("EvaluateFeatureFlagRequest").GetProperty("properties");
        Assert.True(evaluationProperties.TryGetProperty("userId", out _));
        Assert.True(evaluationProperties.TryGetProperty("environment", out _));
        Assert.True(evaluationProperties.TryGetProperty("attributes", out _));
        Assert.False(evaluationProperties.TryGetProperty("currentTime", out _));
    }

    private WebApplicationFactory<Program> CreateApplication()
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                services.PostConfigure<ApiKeyOptions>(options =>
                {
                    options.ManagementKey = "test-management-key";
                    options.EvaluationKey = "test-evaluation-key";
                });
                services.RemoveAll<IDbContextOptionsConfiguration<FlagbitDbContext>>();
                services.RemoveAll<DbContextOptions<FlagbitDbContext>>();
                services.RemoveAll<FlagbitDbContext>();
                services.AddDbContext<FlagbitDbContext>(options => options.UseNpgsql(_postgreSql.ConnectionString));
            });
        });
    }
}
