namespace Flagbit.Infrastructure.Persistence.Entities;

internal sealed class FeatureFlagDependencyEntity
{
    public long Id { get; set; }

    public long FeatureFlagId { get; set; }

    public string DependencyKey { get; set; } = string.Empty;

    public string NormalizedDependencyKey { get; set; } = string.Empty;

    public FeatureFlagEntity FeatureFlag { get; set; } = null!;
}
