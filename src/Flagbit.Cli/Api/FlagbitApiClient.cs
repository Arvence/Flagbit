using System.Net.Http.Json;
using System.Text.Json;

namespace Flagbit.Cli.Api;

internal sealed class FlagbitApiClient
{
    private readonly HttpClient _httpClient;

    public FlagbitApiClient(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
    }

    public async Task<IReadOnlyCollection<FeatureFlagResponse>> GetAllAsync()
    {
        using var response = await _httpClient.GetAsync("api/flags");
        return await ReadAsync<FeatureFlagResponse[]>(response);
    }

    public async Task<FeatureFlagResponse> GetByKeyAsync(string key)
    {
        var path = $"api/flags/{Uri.EscapeDataString(key)}";
        using var response = await _httpClient.GetAsync(path);
        return await ReadAsync<FeatureFlagResponse>(response);
    }

    public async Task<FeatureFlagResponse> CreateAsync(string key)
    {
        using var response = await _httpClient.PostAsJsonAsync("api/flags", new CreateFeatureFlagRequest(key));
        return await ReadAsync<FeatureFlagResponse>(response);
    }

    public async Task<FeatureFlagResponse> SetEnabledAsync(string key, bool isEnabled)
    {
        var action = isEnabled ? "enable" : "disable";
        var path = $"api/flags/{Uri.EscapeDataString(key)}/{action}";
        using var request = new HttpRequestMessage(HttpMethod.Put, path);
        using var response = await _httpClient.SendAsync(request);
        return await ReadAsync<FeatureFlagResponse>(response);
    }

    public async Task DeleteAsync(string key)
    {
        var path = $"api/flags/{Uri.EscapeDataString(key)}";
        using var response = await _httpClient.DeleteAsync(path);
        response.EnsureSuccessStatusCode();
    }

    public async Task<FeatureFlagEvaluationResponse> EvaluateAsync(string key, EvaluateFeatureFlagRequest request)
    {
        var path = $"api/flags/{Uri.EscapeDataString(key)}/evaluate";
        using var response = await _httpClient.PostAsJsonAsync(path, request);
        return await ReadAsync<FeatureFlagEvaluationResponse>(response);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>() ?? throw new JsonException();
    }
}
