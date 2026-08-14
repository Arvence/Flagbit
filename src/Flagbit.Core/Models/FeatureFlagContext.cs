namespace Flagbit.Core.Models;

public sealed record FeatureFlagContext(string? UserId = null, string? Environment = null, DateTimeOffset? CurrentTime = null, IReadOnlyDictionary<string, string>? Attributes = null)
{
    public static FeatureFlagContext Empty { get; } = new();
}
