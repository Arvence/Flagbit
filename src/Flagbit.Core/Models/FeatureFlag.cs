namespace Flagbit.Core.Models;

public sealed class FeatureFlag
{
    public FeatureFlag(string key, bool isEnabled, IEnumerable<string>? targetedUserIds = null, int? rolloutPercentage = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (rolloutPercentage is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(rolloutPercentage));
        }

        Key = key;
        IsEnabled = isEnabled;
        TargetedUserIds = targetedUserIds?
            .Where(userId => !string.IsNullOrWhiteSpace(userId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
        RolloutPercentage = rolloutPercentage;
    }

    public string Key { get; }

    public bool IsEnabled { get; private set; }

    public IReadOnlyCollection<string> TargetedUserIds { get; }

    public int? RolloutPercentage { get; }

    public void Enable()
    {
        IsEnabled = true;
    }

    public void Disable()
    {
        IsEnabled = false;
    }
}
