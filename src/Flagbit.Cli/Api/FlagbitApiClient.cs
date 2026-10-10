using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Flagbit.Cli.Api;

internal sealed class FlagbitApiClient
{
    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;

    public FlagbitApiClient(HttpClient httpClient, string? apiKey = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        if (apiKey is not null && (string.IsNullOrWhiteSpace(apiKey) || apiKey.Any(character => character < ' ' || character > '~')))
        {
            throw new ArgumentException("The API key must be a nonblank printable ASCII header value.", nameof(apiKey));
        }

        _httpClient = httpClient;
        _apiKey = apiKey;

        if (apiKey is not null)
        {
            _httpClient.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        }
    }

    public async Task<IReadOnlyCollection<FeatureFlagResponse>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, "api/flags", null, cancellationToken);
        var flags = await ReadAsync<FeatureFlagResponse[]>(response, cancellationToken);
        foreach (var flag in flags)
        {
            ValidateFlag(flag);
        }

        return flags;
    }

    public async Task<FeatureFlagResponse> GetByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, $"api/flags/{Uri.EscapeDataString(key)}", null, cancellationToken);
        return ValidateFlag(await ReadAsync<FeatureFlagResponse>(response, cancellationToken));
    }

    public async Task<FeatureFlagResponse> CreateAsync(string key, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, "api/flags", JsonContent.Create(new CreateFeatureFlagRequest(key)), cancellationToken);
        return ValidateFlag(await ReadAsync<FeatureFlagResponse>(response, cancellationToken));
    }

    public async Task<FeatureFlagResponse> SetEnabledAsync(string key, bool isEnabled, CancellationToken cancellationToken = default)
    {
        var action = isEnabled ? "enable" : "disable";
        using var response = await SendAsync(HttpMethod.Put, $"api/flags/{Uri.EscapeDataString(key)}/{action}", null, cancellationToken);
        return ValidateFlag(await ReadAsync<FeatureFlagResponse>(response, cancellationToken));
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Delete, $"api/flags/{Uri.EscapeDataString(key)}", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<FeatureFlagResponse> EvaluateAsync(string key, EvaluateFeatureFlagRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, $"api/flags/{Uri.EscapeDataString(key)}/evaluate", JsonContent.Create(request), cancellationToken);
        return ValidateFlag(await ReadAsync<FeatureFlagResponse>(response, cancellationToken));
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        cancellationToken.ThrowIfCancellationRequested();
        return await _httpClient.SendAsync(request, cancellationToken);
    }

    private async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadJsonAsync<T>(response.Content, cancellationToken);
    }

    private static async Task<T> ReadJsonAsync<T>(HttpContent content, CancellationToken cancellationToken)
    {
        try
        {
            return await content.ReadFromJsonAsync<T>(cancellationToken) ?? throw new JsonException();
        }
        catch (InvalidOperationException exception) when (exception.InnerException is ArgumentException)
        {
            throw new JsonException("Invalid response character encoding.", exception);
        }
    }

    private static FeatureFlagResponse ValidateFlag(FeatureFlagResponse? flag)
    {
        if (flag is null || string.IsNullOrWhiteSpace(flag.Key))
        {
            throw new JsonException();
        }

        return flag;
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string? details = null;
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.Conflict
            && response.Content.Headers.ContentType?.MediaType is "application/problem+json" or "application/json")
        {
            try
            {
                using var problem = await ReadJsonAsync<JsonDocument>(response.Content, cancellationToken);
                if (problem.RootElement.ValueKind == JsonValueKind.Object)
                {
                    var messages = new List<string>();
                    var root = problem.RootElement;
                    if (root.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
                    {
                        messages.Add(detail.GetString()!);
                    }
                    else if (root.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)
                    {
                        messages.Add(title.GetString()!);
                    }

                    if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var field in errors.EnumerateObject())
                        {
                            if (field.Value.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var error in field.Value.EnumerateArray())
                                {
                                    if (error.ValueKind == JsonValueKind.String)
                                    {
                                        messages.Add($"{field.Name}: {error.GetString()}");
                                    }
                                }
                            }
                        }
                    }

                    details = string.Join(" ", messages);
                    if (_apiKey is not null)
                    {
                        details = details.Replace(_apiKey, "[redacted]", StringComparison.Ordinal);
                    }

                    details = new string(details.Select(character => char.IsControl(character) ? ' ' : character).ToArray()).Trim();
                    if (details.Length > 1000)
                    {
                        details = details[..1000] + "...";
                    }
                }
            }
            catch (JsonException)
            {
                // Invalid error bodies fall back to the HTTP status.
            }
        }

        throw new ApiRequestException(response.StatusCode, details);
    }
}
