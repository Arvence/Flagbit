using System.Net;
using System.Text.Json;

namespace Flagbit.Cli.Tests;

public sealed class CliApplicationTests
{
    public static TheoryData<string[]> InvalidArguments => new()
    {
        Array.Empty<string>(),
        new[] { "unknown" },
        new[] { "list", "extra" },
        new[] { "get" },
        new[] { "create" },
        new[] { "enable" },
        new[] { "disable" },
        new[] { "delete" },
        new[] { "evaluate" },
        new[] { "get", "flag", "extra" },
        new[] { "create", "" },
        new[] { "get", " " },
        new[] { "enable", "\t" },
        new[] { "disable", " " },
        new[] { "delete", " " },
        new[] { "evaluate", " " },
        new[] { "evaluate", "flag", "--unknown", "value" },
        new[] { "evaluate", "flag", "--user" },
        new[] { "evaluate", "flag", "--environment" },
        new[] { "evaluate", "flag", "--attribute" },
        new[] { "evaluate", "flag", "--user", "" },
        new[] { "evaluate", "flag", "--environment", " " },
        new[] { "evaluate", "flag", "--user", "--environment" },
        new[] { "evaluate", "flag", "--user", "one", "--USER", "two" },
        new[] { "evaluate", "flag", "--environment", "prod", "--environment", "prod" },
        new[] { "evaluate", "flag", "--attribute", "plan" },
        new[] { "evaluate", "flag", "--attribute", "=pro" },
        new[] { "evaluate", "flag", "--attribute", "plan=" },
        new[] { "evaluate", "flag", "--attribute", " =pro" },
        new[] { "evaluate", "flag", "--attribute", "plan= " },
        new[] { "evaluate", "flag", "--attribute", "plan=pro", "--attribute", "plan=pro" },
        new[] { "evaluate", "flag", "--attribute", "plan=pro", "--attribute", "PLAN=free" }
    };

    [Theory]
    [MemberData(nameof(InvalidArguments))]
    public async Task InvalidArgumentsFailBeforeSendingRequests(string[] args)
    {
        using var cli = new CliTestSession((_, _) => throw new InvalidOperationException("No request should be sent."));

        Assert.Equal(1, await cli.Application.RunAsync(args));
        Assert.Equal(0, cli.Handler.CallCount);
        Assert.Contains("Usage:", cli.Output.ToString());
        if (args.Length > 0)
        {
            Assert.NotEmpty(cli.Error.ToString());
        }
    }

    [Fact]
    public async Task EvaluationPreservesDistinctAttributesExtraEqualsAndEscapedKey()
    {
        const string key = "checkout ?#+%\u6771\u4eac";
        using var cli = new CliTestSession(async (request, cancellationToken) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal($"/prefix/api/flags/{Uri.EscapeDataString(key)}/evaluate", request.RequestUri!.PathAndQuery);
            Assert.Equal("evaluation-secret", Assert.Single(request.Headers.GetValues("X-Api-Key")));
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Assert.Equal("user ?&=", body.RootElement.GetProperty("userId").GetString());
            Assert.Equal(" Production ", body.RootElement.GetProperty("environment").GetString());
            var attributes = body.RootElement.GetProperty("attributes");
            Assert.Equal(3, attributes.EnumerateObject().Count());
            Assert.Equal("a=b=c", attributes.GetProperty("token").GetString());
            Assert.Equal("Pro", attributes.GetProperty("Plan").GetString());
            Assert.Equal(" Enterprise ", attributes.GetProperty(" Plan ").GetString());
            return CliTestSession.Response(JsonSerializer.Serialize(new { key, isEnabled = true }));
        }, "evaluation-secret");

        Assert.Equal(0, await cli.Application.RunAsync(["EVALUATE", key, "--USER", "user ?&=", "--environment", " Production ", "--attribute", "token=a=b=c", "--attribute", "Plan=Pro", "--attribute", " Plan = Enterprise "]));
        Assert.Equal($"{key} is enabled.", cli.Output.ToString().Trim());
        Assert.Empty(cli.Error.ToString());
        Assert.Equal(1, cli.Handler.CallCount);
    }

    [Theory]
    [InlineData("[]", "No feature flags found.")]
    [InlineData("[{\"key\":\"zebra\",\"isEnabled\":false},{\"key\":\"Alpha\",\"isEnabled\":true}]", "Alpha enabled\nzebra disabled")]
    public async Task ListFormatsEmptyAndSortedResults(string body, string expected)
    {
        using var cli = new CliTestSession((_, _) => Task.FromResult(CliTestSession.Response(body)));

        Assert.Equal(0, await cli.Application.RunAsync(["list"]));
        Assert.Equal(expected, cli.Output.ToString().Replace("\r\n", "\n").Trim());
        Assert.Empty(cli.Error.ToString());
    }

    [Theory]
    [InlineData(401, "API authentication failed. Set FLAGBIT_API_KEY to a valid API key.")]
    [InlineData(403, "API access denied. This command requires a management API key.")]
    public async Task AuthenticationErrorsRemainActionable(int status, string expected)
    {
        using var cli = new CliTestSession((_, _) => Task.FromResult(CliTestSession.Response("{\"detail\":\"private-secret\"}", (HttpStatusCode)status, "application/problem+json")));

        Assert.Equal(1, await cli.Application.RunAsync(["list"]));
        Assert.Equal(expected, cli.Error.ToString().Trim());
        Assert.Empty(cli.Output.ToString());
    }

    [Theory]
    [InlineData(400, "{\"title\":\"Validation failed.\",\"errors\":{\"key\":[\"Key is required.\"]}}", "API request failed: 400 BadRequest. Validation failed. key: Key is required.")]
    [InlineData(409, "{\"title\":\"Conflict\",\"detail\":\"Flag already exists.\"}", "API request failed: 409 Conflict. Flag already exists.")]
    [InlineData(404, "{\"detail\":\"Flag was not found.\"}", "API request failed: 404 NotFound. Flag was not found.")]
    public async Task ProblemDetailsIncludeUsefulMessages(int status, string body, string expected)
    {
        using var cli = new CliTestSession((_, _) => Task.FromResult(CliTestSession.Response(body, (HttpStatusCode)status, "application/problem+json")));

        Assert.Equal(1, await cli.Application.RunAsync(["delete", "flag"]));
        Assert.Equal(expected, cli.Error.ToString().Trim());
        Assert.Empty(cli.Output.ToString());
    }

    [Theory]
    [InlineData(409, "<html>private-secret</html>", "text/html", "Conflict")]
    [InlineData(400, "{broken", "application/problem+json", "BadRequest")]
    [InlineData(400, "null", "application/problem+json", "BadRequest")]
    [InlineData(400, "[]", "application/problem+json", "BadRequest")]
    [InlineData(400, "{\"errors\":{\"key\":123},\"detail\":false}", "application/problem+json", "BadRequest")]
    [InlineData(500, "{\"detail\":\"private-secret\"}", "application/problem+json", "InternalServerError")]
    public async Task UnusableOrServerErrorBodiesFallBackToStatus(int status, string body, string mediaType, string name)
    {
        using var cli = new CliTestSession((_, _) => Task.FromResult(CliTestSession.Response(body, (HttpStatusCode)status, mediaType)));

        Assert.Equal(1, await cli.Application.RunAsync(["get", "flag"]));
        Assert.Equal($"API request failed: {status} {name}.", cli.Error.ToString().Trim());
        Assert.Empty(cli.Output.ToString());
    }

    [Fact]
    public async Task ProblemDetailsRedactTheConfiguredKeyAndRemoveControlCharacters()
    {
        using var cli = new CliTestSession((_, _) => Task.FromResult(CliTestSession.Response("{\"detail\":\"private-secret\\r\\n\\u001b[31mconflict\"}", HttpStatusCode.Conflict)), "private-secret");

        Assert.Equal(1, await cli.Application.RunAsync(["create", "flag"]));
        Assert.Equal("API request failed: 409 Conflict. [redacted]   [31mconflict", cli.Error.ToString().Trim());
        Assert.Empty(cli.Output.ToString());
    }

    [Fact]
    public async Task ConnectionFailuresDoNotLeakExceptionDetails()
    {
        using var cli = new CliTestSession((_, _) => throw new HttpRequestException("private-secret"));

        Assert.Equal(1, await cli.Application.RunAsync(["list"]));
        Assert.Equal("Could not connect to the Flagbit API.", cli.Error.ToString().Trim());
        Assert.Empty(cli.Output.ToString());
    }
}
