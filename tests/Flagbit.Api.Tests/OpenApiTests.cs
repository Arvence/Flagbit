using System.Net;
using System.Data.Common;
using System.Text.Json;
using Flagbit.Api.Authentication;
using Flagbit.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flagbit.Api.Tests;

public sealed class OpenApiTests
{
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

    [Fact]
    public async Task ProtectedOperationsDescribeSecurityAndErrorResponses()
    {
        using var application = CreateApplication();
        using var client = application.CreateClient();
        using var json = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var scheme = json.RootElement.GetProperty("components").GetProperty("securitySchemes").GetProperty("ApiKey");
        Assert.Equal("apiKey", scheme.GetProperty("type").GetString());
        Assert.Equal("header", scheme.GetProperty("in").GetString());
        Assert.Equal("X-Api-Key", scheme.GetProperty("name").GetString());
        var paths = json.RootElement.GetProperty("paths");
        var protectedOperations = paths.EnumerateObject().Where(path => path.Name.StartsWith("/api/", StringComparison.Ordinal))
            .SelectMany(path => path.Value.EnumerateObject()).ToArray();
        Assert.Equal(12, protectedOperations.Length);

        foreach (var operation in protectedOperations)
        {
            Assert.Empty(Assert.Single(operation.Value.GetProperty("security").EnumerateArray()).GetProperty("ApiKey").EnumerateArray());
            var responses = operation.Value.GetProperty("responses");
            Assert.False(responses.GetProperty("401").TryGetProperty("content", out _));
            Assert.False(responses.GetProperty("403").TryGetProperty("content", out _));
            AssertProblemSchema(responses.GetProperty("500"));
        }

        Assert.False(paths.GetProperty("/").GetProperty("get").TryGetProperty("security", out _));
        foreach (var path in new[] { "/api/flags", "/api/keys" })
        {
            var responses = paths.GetProperty(path).GetProperty("post").GetProperty("responses");
            var alternatives = responses.GetProperty("400").GetProperty("content").GetProperty("application/problem+json")
                .GetProperty("schema").GetProperty("anyOf").EnumerateArray().Select(schema => schema.TryGetProperty("$ref", out var reference)
                    ? json.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(reference.GetString()!.Split('/').Last()) : schema).ToArray();
            Assert.Equal(2, alternatives.Length);
            Assert.All(alternatives, schema => Assert.True(schema.GetProperty("properties").TryGetProperty("status", out _)));
            Assert.Contains(alternatives, schema => schema.GetProperty("properties").TryGetProperty("errors", out _));
            Assert.Contains(alternatives, schema => !schema.GetProperty("properties").TryGetProperty("errors", out _));
            AssertProblemSchema(responses.GetProperty("415"));
        }

        Assert.Contains("management", paths.GetProperty("/api/keys").GetProperty("post").GetProperty("description").GetString());
        Assert.Contains("generated application", paths.GetProperty("/api/flags/{key}/evaluate").GetProperty("post").GetProperty("description").GetString());
    }

    [Fact]
    public async Task ApplicationKeysDescribeOneTimeDisclosureAndRevocation()
    {
        using var application = CreateApplication();
        using var client = application.CreateClient();
        using var json = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var paths = json.RootElement.GetProperty("paths");
        var creation = paths.GetProperty("/api/keys").GetProperty("post").GetProperty("responses");
        Assert.Equal("#/components/schemas/CreatedApiKeyResponse", creation.GetProperty("201").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());
        Assert.False(creation.TryGetProperty("200", out _));
        var listing = paths.GetProperty("/api/keys").GetProperty("get").GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema");
        Assert.Equal("array", listing.GetProperty("type").GetString());
        Assert.Equal("#/components/schemas/ApiKeyResponse", listing.GetProperty("items").GetProperty("$ref").GetString());
        var revocation = paths.GetProperty("/api/keys/{id}").GetProperty("delete").GetProperty("responses");
        Assert.False(revocation.GetProperty("204").TryGetProperty("content", out _));
        AssertProblemSchema(revocation.GetProperty("404"));
        var schemas = json.RootElement.GetProperty("components").GetProperty("schemas");
        Assert.True(schemas.GetProperty("CreatedApiKeyResponse").GetProperty("properties").TryGetProperty("key", out _));
        var listedProperties = schemas.GetProperty("ApiKeyResponse").GetProperty("properties");
        Assert.True(listedProperties.TryGetProperty("id", out _));
        Assert.True(listedProperties.TryGetProperty("name", out _));
        Assert.True(listedProperties.TryGetProperty("createdAt", out _));
        Assert.False(listedProperties.TryGetProperty("key", out _));
        Assert.DoesNotContain("hash", schemas.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Development", HttpStatusCode.OK)]
    [InlineData("Production", HttpStatusCode.NotFound)]
    [InlineData("Staging", HttpStatusCode.NotFound)]
    public async Task DocumentIsAvailableOnlyInDevelopment(string environment, HttpStatusCode status)
    {
        using var application = CreateApplication(environment);
        using var client = application.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(status, response.StatusCode);
    }

    private static void AssertProblemSchema(JsonElement response)
    {
        Assert.Equal("#/components/schemas/ProblemDetails", response.GetProperty("content").GetProperty("application/problem+json").GetProperty("schema").GetProperty("$ref").GetString());
    }

    private static WebApplicationFactory<Program> CreateApplication(string environment = "Development")
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
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
                services.AddDbContext<FlagbitDbContext>(options => options.UseNpgsql("Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused;Timeout=1").AddInterceptors(new RejectDatabaseConnection()));
            });
        });
    }

    private sealed class RejectDatabaseConnection : DbConnectionInterceptor
    {
        public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
        {
            throw new InvalidOperationException("OpenAPI generation must not open a database connection.");
        }

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("OpenAPI generation must not open a database connection.");
        }
    }
}
