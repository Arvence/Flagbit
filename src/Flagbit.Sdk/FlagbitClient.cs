using System.Net.Http.Json;
using System.Text.Json;

namespace Flagbit.Sdk;

public sealed class FlagbitClient
{
    private readonly HttpClient _httpClient;

    public FlagbitClient(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        if (httpClient.BaseAddress is not { IsAbsoluteUri: true } baseAddress
            || (baseAddress.Scheme != Uri.UriSchemeHttp && baseAddress.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("The HTTP client must have an absolute HTTP or HTTPS base address.", nameof(httpClient));
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

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        return await SendEvaluationAsync(request, cancellationToken);
    }

    public async Task<bool> EvaluateAsync(string key, FeatureFlagEvaluationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(context);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/flags/{Uri.EscapeDataString(key)}/evaluate")
        {
            Content = JsonContent.Create(context)
        };
        return await SendEvaluationAsync(request, cancellationToken);
    }

    public async Task<T> GetVariationAsync<T>(string key, T enabledVariation, T disabledVariation, string? userId = null, CancellationToken cancellationToken = default)
    {
        return await IsEnabledAsync(key, userId, cancellationToken) ? enabledVariation : disabledVariation;
    }

    public async Task<T> GetContextualVariationAsync<T>(string key, T enabledVariation, T disabledVariation, FeatureFlagEvaluationContext context, CancellationToken cancellationToken = default)
    {
        return await EvaluateAsync(key, context, cancellationToken) ? enabledVariation : disabledVariation;
    }

    private async Task<bool> SendEvaluationAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var evaluation = await response.Content.ReadFromJsonAsync<FeatureFlagEvaluationResponse>(cancellationToken);
        if (evaluation is null || string.IsNullOrWhiteSpace(evaluation.Key) || evaluation.IsEnabled is null)
        {
            throw new JsonException("The Flagbit API returned an invalid evaluation response.");
        }

        return evaluation.IsEnabled.Value;
    }
}
