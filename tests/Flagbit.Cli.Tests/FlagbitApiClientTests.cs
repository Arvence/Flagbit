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

    public static IEnumerable<object[]> InvalidResponses()
    {
        string[] invalidFlags = ["null", "{}", "{\"key\":\"flag\"}", "{\"isEnabled\":true}", "{\"key\":null,\"isEnabled\":true}", "{\"key\":\" \",\"isEnabled\":false}", "{\"key\":\"flag\",\"isEnabled\":null}", "{\"key\":\"flag\",\"isEnabled\":\"false\"}", "{broken", "[]"];
        foreach (var body in invalidFlags)
        {
            yield return [new[] { "get", "flag" }, body];
            yield return [new[] { "evaluate", "flag" }, body];
        }

        foreach (var command in new[] { "create", "enable", "disable" })
        {
            yield return [new[] { command, "flag" }, "{\"key\":\"flag\"}"];
        }

        foreach (var body in new[] { "null", "{}", "[null]", "[{}]", "[{\"key\":\"flag\"}]", "[{\"key\":\"valid\",\"isEnabled\":true},null]" })
        {
            yield return [new[] { "list" }, body];
        }
    }

    [Theory]
    [MemberData(nameof(InvalidResponses))]
    public async Task StructurallyInvalidSuccessFailsWithoutPrintingAFlag(string[] args, string body)
    {
        using var cli = new CliTestSession((_, _) => Task.FromResult(CliTestSession.Response(body)));

        Assert.Equal(1, await cli.Application.RunAsync(args));
        Assert.Equal("The Flagbit API returned an invalid response.", cli.Error.ToString().Trim());
        Assert.Empty(cli.Output.ToString());
    }

    [Theory]
    [InlineData("get")]
    [InlineData("evaluate")]
    public async Task ExplicitFalseIsAValidSuccessfulResponse(string command)
    {
        using var cli = new CliTestSession((_, _) => Task.FromResult(CliTestSession.Response("{\"key\":\"flag\",\"isEnabled\":false}")));

        Assert.Equal(0, await cli.Application.RunAsync([command, "flag"]));
        Assert.Equal("flag is disabled.", cli.Output.ToString().Trim());
        Assert.Empty(cli.Error.ToString());
    }

    [Theory]
    [InlineData(200, "The Flagbit API returned an invalid response.")]
    [InlineData(409, "API request failed: 409 Conflict.")]
    public async Task InvalidResponseEncodingProducesAControlledError(int status, string expected)
    {
        using var cli = new CliTestSession((_, _) =>
        {
            var response = CliTestSession.Response("{\"key\":\"flag\",\"isEnabled\":true}", (HttpStatusCode)status);
            response.Content.Headers.ContentType!.CharSet = "invalid-charset";
            return Task.FromResult(response);
        });

        Assert.Equal(1, await cli.Application.RunAsync(["get", "flag"]));
        Assert.Equal(expected, cli.Error.ToString().Trim());
        Assert.Empty(cli.Output.ToString());
    }

    [Fact]
    public async Task ProblemDetailsOutputIsBounded()
    {
        var body = JsonSerializer.Serialize(new { detail = new string('x', 2000) });
        using var cli = new CliTestSession((_, _) => Task.FromResult(CliTestSession.Response(body, HttpStatusCode.BadRequest)));

        Assert.Equal(1, await cli.Application.RunAsync(["create", "flag"]));
        Assert.Equal("API request failed: 400 BadRequest. " + new string('x', 1000) + "...", cli.Error.ToString().Trim());
    }
}
