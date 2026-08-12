namespace Flagbit.Core.Models;

public sealed record FeatureFlagContext(string? UserId = null, string? Environment = null, DateTimeOffset? CurrentTime = null)
{
    public static FeatureFlagContext Empty { get; } = new();
}
