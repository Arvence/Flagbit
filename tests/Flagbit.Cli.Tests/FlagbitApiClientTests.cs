extern alias FlagbitCli;

using System.Net;
using System.Text.Json;
using FlagbitApiClient = FlagbitCli::Flagbit.Cli.Api.FlagbitApiClient;

namespace Flagbit.Cli.Tests;

public sealed class FlagbitApiClientTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("secret\r\nInjected: value")]
    [InlineData("secret\t")]
    [InlineData("secret\u007f")]
    [InlineData("secret\u00e9")]
    public void InvalidHeaderValuesAreRejectedWithoutLeakingThem(string key)
    {
        using var httpClient = new HttpClient();

        var exception = Assert.Throws<ArgumentException>(() => new FlagbitApiClient(httpClient, key));

        Assert.DoesNotContain("secret", exception.Message);
        Assert.False(httpClient.DefaultRequestHeaders.Contains("X-Api-Key"));
    }

    [Theory]
    [InlineData("get", "GET", "")]
    [InlineData("enable", "PUT", "/enable")]
    [InlineData("disable", "PUT", "/disable")]
    [InlineData("delete", "DELETE", "")]
    [InlineData("create", "POST", "")]
    public async Task ManagementCommandsPreserveAndEscapeKeys(string command, string method, string suffix)
    {
        const string key = " flag ?#+%\u6771\u4eac ";
        using var cli = new CliTestSession(async (request, cancellationToken) =>
        {
            Assert.Equal(method, request.Method.Method);
            Assert.Equal("management-secret", Assert.Single(request.Headers.GetValues("X-Api-Key")));
            if (command == "create")
            {
                Assert.Equal("/prefix/api/flags", request.RequestUri!.PathAndQuery);
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                Assert.Equal(key, body.RootElement.GetProperty("key").GetString());
            }
            else
            {
                Assert.Equal($"/prefix/api/flags/{Uri.EscapeDataString(key)}{suffix}", request.RequestUri!.PathAndQuery);
            }

            return CliTestSession.Response(JsonSerializer.Serialize(new { key, isEnabled = false }));
        }, "management-secret");

        Assert.Equal(0, await cli.Application.RunAsync([command, key]));
        Assert.Equal(1, cli.Handler.CallCount);
        Assert.Empty(cli.Error.ToString());
    }
}
