namespace Flagbit.Core.Exceptions;

public sealed class FeatureFlagAlreadyExistsException : InvalidOperationException
{
    public FeatureFlagAlreadyExistsException(string key)
        : base($"A feature flag with the key '{key}' already exists.")
    {
        Key = key;
    }

    public string Key { get; }
}

public sealed class FeatureFlagNotFoundException : KeyNotFoundException
{
    public FeatureFlagNotFoundException(string key)
        : base($"A feature flag with the key '{key}' does not exist.")
    {
        Key = key;
    }

    public string Key { get; }
}
