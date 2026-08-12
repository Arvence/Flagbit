using Flagbit.Core;

namespace Flagbit.Core.Tests;

public sealed class FeatureFlagEvaluatorTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IsEnabledAsyncReturnsStoredState(bool storedState)
    {
        var evaluator = new FeatureFlagEvaluator(
            new StubFeatureFlagStore(new FeatureFlag("new-checkout", storedState)));

        var isEnabled = await evaluator.IsEnabledAsync("new-checkout");

        Assert.Equal(storedState, isEnabled);
    }

    [Fact]
    public async Task IsEnabledAsyncReturnsFalseWhenFlagDoesNotExist()
    {
        var evaluator = new FeatureFlagEvaluator(new StubFeatureFlagStore(null));

        var isEnabled = await evaluator.IsEnabledAsync("unknown-feature");

        Assert.False(isEnabled);
    }

    [Fact]
    public async Task IsEnabledAsyncRejectsMissingKey()
    {
        var evaluator = new FeatureFlagEvaluator(new StubFeatureFlagStore(null));

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await evaluator.IsEnabledAsync(" "));
    }

    [Fact]
    public void ConstructorRejectsMissingStore()
    {
        Assert.Throws<ArgumentNullException>(() => new FeatureFlagEvaluator(null!));
    }

    private sealed class StubFeatureFlagStore(FeatureFlag? flag) : IFeatureFlagStore
    {
        public ValueTask<FeatureFlag?> GetByKeyAsync(string key, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(flag);
        }

        public ValueTask<IReadOnlyCollection<FeatureFlag>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask AddAsync(FeatureFlag flag, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask UpdateAsync(FeatureFlag flag, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
