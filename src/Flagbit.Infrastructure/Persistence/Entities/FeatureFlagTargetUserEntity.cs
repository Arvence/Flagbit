namespace Flagbit.Infrastructure.Persistence.Entities;

internal sealed class FeatureFlagTargetUserEntity
{
    public long Id { get; set; }

    public long FeatureFlagId { get; set; }

    public string UserId { get; set; } = string.Empty;

    public string NormalizedUserId { get; set; } = string.Empty;

    public FeatureFlagEntity FeatureFlag { get; set; } = null!;
}
