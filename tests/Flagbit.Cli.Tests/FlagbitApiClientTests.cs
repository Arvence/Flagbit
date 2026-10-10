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
}
