namespace Flagbit.Infrastructure.Persistence.Entities;

internal sealed class FeatureFlagEnvironmentEntity
{
    public long Id { get; set; }

    public long FeatureFlagId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string NormalizedName { get; set; } = string.Empty;

    public FeatureFlagEntity FeatureFlag { get; set; } = null!;
}
