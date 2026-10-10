extern alias FlagbitCli;

using System.Diagnostics;
using CliApplication = FlagbitCli::Flagbit.Cli.CliApplication;

namespace Flagbit.Cli.Tests;

public sealed class CliExecutableTests
{
    [Theory]
    [InlineData("ftp://example.com")]
    [InlineData("file:///C:/flags")]
    [InlineData("mailto:private-secret@example.com")]
    [InlineData("relative")]
    [InlineData("")]
    [InlineData("http://user:private-secret@example.com")]
    [InlineData("http://localhost/?key=private-secret")]
    [InlineData("https://localhost/#private-secret")]
    public async Task InvalidUrlsProduceControlledExitCodes(string url)
    {
        var result = await RunAsync(url, null, "list");

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("FLAGBIT_API_URL must be an absolute HTTP(S) URL without credentials, a query, or a fragment.", result.Error.Trim());
        Assert.Empty(result.Output);
    }

    [Theory]
    [InlineData("private-secret\r\nInjected: value")]
    [InlineData("private-secret\t")]
    [InlineData("private-secret\u00e9")]
    [InlineData(" ")]
    public async Task InvalidApiKeysProduceControlledExitCodes(string key)
    {
        var result = await RunAsync("https://localhost", key, "list");

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("FLAGBIT_API_KEY must be a nonblank HTTP header value containing only printable ASCII characters.", result.Error.Trim());
        Assert.Empty(result.Output);
    }

    [Fact]
    public async Task MissingArgumentsFailWithoutAnApiConnection()
    {
        var result = await RunAsync("http://127.0.0.1:1", null, "get");

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("Unknown command or incorrect arguments.", result.Error.Trim());
        Assert.Contains("Usage:", result.Output);
    }

    [Fact]
    public async Task ConnectionFailureProducesAControlledExitCode()
    {
        var result = await RunAsync("http://127.0.0.1:1", null, "list");

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("Could not connect to the Flagbit API.", result.Error.Trim());
        Assert.Empty(result.Output);
    }

    internal static async Task<(int ExitCode, string Output, string Error)> RunAsync(string apiUrl, string? apiKey, params string[] args)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(typeof(CliApplication).Assembly.Location);
        foreach (var argument in args)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["FLAGBIT_API_URL"] = apiUrl;
        start.Environment.Remove("FLAGBIT_API_KEY");
        if (apiKey is not null)
        {
            start.Environment["FLAGBIT_API_KEY"] = apiKey;
        }

        using var process = Process.Start(start)!;
        try
        {
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await process.WaitForExitAsync(timeout.Token);
            return (process.ExitCode, await output, await error);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }
}
