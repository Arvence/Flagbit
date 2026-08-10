namespace Flagbit.Core;

/// <summary>
/// A named switch that controls whether a feature is available.
/// </summary>
public sealed class FeatureFlag
{
    public FeatureFlag(string key, bool isEnabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        Key = key;
        IsEnabled = isEnabled;
    }

    public string Key { get; }

    public bool IsEnabled { get; private set; }

    public void Enable()
    {
        IsEnabled = true;
    }

    public void Disable()
    {
        IsEnabled = false;
    }
}
