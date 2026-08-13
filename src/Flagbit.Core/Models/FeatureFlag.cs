namespace Flagbit.Core.Models;

public sealed class FeatureFlag
{
    public FeatureFlag(string key, bool isEnabled, IEnumerable<string>? targetedUserIds = null, int? rolloutPercentage = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        Key = key;
        IsEnabled = isEnabled;
        ConfigureEvaluation(targetedUserIds, rolloutPercentage);
    }

    public string Key { get; }

    public bool IsEnabled { get; private set; }

    public IReadOnlyCollection<string> TargetedUserIds { get; private set; } = [];

    public int? RolloutPercentage { get; private set; }

    public void Enable()
    {
        IsEnabled = true;
    }

    public void Disable()
    {
        IsEnabled = false;
    }

    public void ConfigureEvaluation(IEnumerable<string>? targetedUserIds, int? rolloutPercentage)
    {
        if (rolloutPercentage is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(rolloutPercentage));
        }

        TargetedUserIds = targetedUserIds?
            .Where(userId => !string.IsNullOrWhiteSpace(userId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
        RolloutPercentage = rolloutPercentage;
    }
}
