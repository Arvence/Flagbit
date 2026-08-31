using System.Net.Http.Json;
using System.Text.Json;

namespace Flagbit.Sdk;

public sealed class FlagbitClient
{
    private readonly HttpClient _httpClient;

    public FlagbitClient(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        if (httpClient.BaseAddress is null)
        {
            throw new ArgumentException("The HTTP client must have a base address.", nameof(httpClient));
        }

        _httpClient = httpClient;
    }

    public async Task<bool> IsEnabledAsync(string key, string? userId = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var path = $"api/flags/{Uri.EscapeDataString(key)}/enabled";

        if (userId is not null)
        {
            path += $"?userId={Uri.EscapeDataString(userId)}";
        }

        using var response = await _httpClient.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();

        var evaluation = await response.Content.ReadFromJsonAsync<FeatureFlagEvaluationResponse>(cancellationToken);
        return evaluation?.IsEnabled ?? throw new JsonException("The Flagbit API returned an invalid evaluation response.");
    }

    public async Task<T> GetVariationAsync<T>(string key, T enabledVariation, T disabledVariation, string? userId = null, CancellationToken cancellationToken = default)
    {
        return await IsEnabledAsync(key, userId, cancellationToken) ? enabledVariation : disabledVariation;
    }
}
