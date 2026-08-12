using Flagbit.Core.Abstractions;
using Flagbit.Core.Exceptions;
using Flagbit.Core.Models;

namespace Flagbit.Infrastructure;

public sealed class FeatureFlagStore : IFeatureFlagStore
{
    private readonly Dictionary<string, FeatureFlag> _flags = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    public ValueTask<FeatureFlag?> GetByKeyAsync(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        lock (_sync)
        {
            _flags.TryGetValue(key, out var flag);
            return ValueTask.FromResult(flag);
        }
    }

    public ValueTask<IReadOnlyCollection<FeatureFlag>> GetAllAsync()
    {
        lock (_sync)
        {
            IReadOnlyCollection<FeatureFlag> flags = _flags.Values
                .OrderBy(flag => flag.Key, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return ValueTask.FromResult(flags);
        }
    }

    public ValueTask AddAsync(FeatureFlag flag)
    {
        ArgumentNullException.ThrowIfNull(flag);

        lock (_sync)
        {
            if (!_flags.TryAdd(flag.Key, flag))
            {
                throw new FeatureFlagAlreadyExistsException(flag.Key);
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask UpdateAsync(FeatureFlag flag)
    {
        ArgumentNullException.ThrowIfNull(flag);

        lock (_sync)
        {
            if (!_flags.ContainsKey(flag.Key))
            {
                throw new FeatureFlagNotFoundException(flag.Key);
            }

            _flags[flag.Key] = flag;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> DeleteAsync(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        lock (_sync)
        {
            return ValueTask.FromResult(_flags.Remove(key));
        }
    }
}
