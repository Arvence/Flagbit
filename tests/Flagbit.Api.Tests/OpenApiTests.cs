using System.Net;
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
    }

    private WebApplicationFactory<Program> CreateApplication()
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDbContextOptionsConfiguration<FlagbitDbContext>>();
                services.RemoveAll<DbContextOptions<FlagbitDbContext>>();
                services.RemoveAll<FlagbitDbContext>();
                services.AddDbContext<FlagbitDbContext>(options => options.UseNpgsql(_postgreSql.ConnectionString));
            });
        });
    }
}
