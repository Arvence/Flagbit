namespace Flagbit.Core.Models;

public sealed class FeatureFlag
{
    public FeatureFlag(string key, bool isEnabled, IEnumerable<string>? targetedUserIds = null, int? rolloutPercentage = null, IEnumerable<string>? environments = null, IEnumerable<FeatureFlagRule>? rules = null, DateTimeOffset? startsAt = null, DateTimeOffset? endsAt = null, IEnumerable<string>? dependencyKeys = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        Key = key;
        IsEnabled = isEnabled;
        ConfigureEvaluation(targetedUserIds, rolloutPercentage, environments, rules, startsAt, endsAt, dependencyKeys);
    }

    public string Key { get; }

    public bool IsEnabled { get; private set; }

    public IReadOnlyCollection<string> TargetedUserIds { get; private set; } = [];

    public int? RolloutPercentage { get; private set; }

    public IReadOnlyCollection<string> Environments { get; private set; } = [];

    public IReadOnlyCollection<FeatureFlagRule> Rules { get; private set; } = [];

    public DateTimeOffset? StartsAt { get; private set; }

    public DateTimeOffset? EndsAt { get; private set; }

    public IReadOnlyCollection<string> DependencyKeys { get; private set; } = [];

    public void Enable()
    {
        IsEnabled = true;
    }

    public void Disable()
    {
        IsEnabled = false;
    }

    public void ConfigureEvaluation(IEnumerable<string>? targetedUserIds, int? rolloutPercentage, IEnumerable<string>? environments = null, IEnumerable<FeatureFlagRule>? rules = null, DateTimeOffset? startsAt = null, DateTimeOffset? endsAt = null, IEnumerable<string>? dependencyKeys = null)
    {
        if (rolloutPercentage is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(rolloutPercentage));
        }

        if (startsAt > endsAt)
        {
            throw new ArgumentException("The evaluation start time cannot be later than the end time.", nameof(startsAt));
        }

        var configuredTargetedUserIds = targetedUserIds?
            .Where(userId => !string.IsNullOrWhiteSpace(userId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
        var configuredEnvironments = environments?
            .Where(environment => !string.IsNullOrWhiteSpace(environment))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
        var configuredRules = rules?.ToArray() ?? [];
        var configuredDependencyKeys = dependencyKeys?
            .Where(dependencyKey => !string.IsNullOrWhiteSpace(dependencyKey))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

        if (configuredRules.Any(rule => rule is null))
        {
            throw new ArgumentException("Evaluation rules cannot contain null entries.", nameof(rules));
        }

        if (configuredDependencyKeys.Contains(Key, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException("A feature flag cannot depend on itself.", nameof(dependencyKeys));
        }

        TargetedUserIds = configuredTargetedUserIds;
        RolloutPercentage = rolloutPercentage;
        Environments = configuredEnvironments;
        Rules = configuredRules;
        StartsAt = startsAt;
        EndsAt = endsAt;
        DependencyKeys = configuredDependencyKeys;
    }
}
