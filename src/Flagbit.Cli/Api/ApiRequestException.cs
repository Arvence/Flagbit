using System.Net;

namespace Flagbit.Cli.Api;

internal sealed class ApiRequestException : HttpRequestException
{
    public ApiRequestException(HttpStatusCode statusCode, string? details) : base("The API request failed.", null, statusCode)
    {
        Details = details;
    }

    public string? Details { get; }
}
