using System.Text.Json.Serialization;

namespace Flagbit.Cli.Api;

internal sealed record CreateFeatureFlagRequest(string Key);

internal sealed record FeatureFlagResponse([property: JsonRequired] string Key, [property: JsonRequired] bool IsEnabled);

internal sealed record EvaluateFeatureFlagRequest(string? UserId = null, string? Environment = null, IReadOnlyDictionary<string, string>? Attributes = null);
