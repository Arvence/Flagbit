namespace Flagbit.Infrastructure.Persistence.Entities;

internal sealed class FeatureFlagEntity
{
    public long Id { get; set; }

    public string Key { get; set; } = string.Empty;

    public string NormalizedKey { get; set; } = string.Empty;

    public bool IsEnabled { get; set; }

    public int? RolloutPercentage { get; set; }

    public DateTimeOffset? StartsAt { get; set; }

    public DateTimeOffset? EndsAt { get; set; }

    public ICollection<FeatureFlagTargetUserEntity> TargetUsers { get; set; } = [];

    public ICollection<FeatureFlagEnvironmentEntity> Environments { get; set; } = [];

    public ICollection<FeatureFlagRuleEntity> Rules { get; set; } = [];

    public ICollection<FeatureFlagDependencyEntity> Dependencies { get; set; } = [];
}
