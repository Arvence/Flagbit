using Flagbit.Core.Abstractions;
using Flagbit.Core.Models;
using Flagbit.Core.Services;

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

    [Theory]
    [InlineData("user-123", true)]
    [InlineData("USER-123", true)]
    [InlineData("user-456", false)]
    [InlineData(null, false)]
    public async Task IsEnabledAsyncMatchesTargetedUsers(string? userId, bool expected)
    {
        var flag = new FeatureFlag("new-checkout", true, targetedUserIds: ["user-123"]);
        var evaluator = new FeatureFlagEvaluator(new StubFeatureFlagStore(flag));

        var isEnabled = await evaluator.IsEnabledAsync("new-checkout", new FeatureFlagContext(UserId: userId));

        Assert.Equal(expected, isEnabled);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(100, true)]
    public async Task IsEnabledAsyncAppliesPercentageBoundaries(int percentage, bool expected)
    {
        var flag = new FeatureFlag("new-checkout", true, rolloutPercentage: percentage);
        var evaluator = new FeatureFlagEvaluator(new StubFeatureFlagStore(flag));

        var isEnabled = await evaluator.IsEnabledAsync("new-checkout", new FeatureFlagContext(UserId: "user-123"));

        Assert.Equal(expected, isEnabled);
    }

    [Fact]
    public async Task IsEnabledAsyncAssignsUsersToPercentageConsistently()
    {
        var flag = new FeatureFlag("new-checkout", true, rolloutPercentage: 30);
        var evaluator = new FeatureFlagEvaluator(new StubFeatureFlagStore(flag));

        var firstResult = await evaluator.IsEnabledAsync("new-checkout", new FeatureFlagContext(UserId: "user-123"));
        var secondResult = await evaluator.IsEnabledAsync("new-checkout", new FeatureFlagContext(UserId: "user-123"));

        Assert.Equal(firstResult, secondResult);
    }

    [Fact]
    public async Task IsEnabledAsyncReturnsFalseWhenPercentageRequiresMissingUser()
    {
        var flag = new FeatureFlag("new-checkout", true, rolloutPercentage: 30);
        var evaluator = new FeatureFlagEvaluator(new StubFeatureFlagStore(flag));

        var isEnabled = await evaluator.IsEnabledAsync("new-checkout", FeatureFlagContext.Empty);

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
        public ValueTask<FeatureFlag?> GetByKeyAsync(string key)
        {
            return ValueTask.FromResult(flag);
        }

        public ValueTask<IReadOnlyCollection<FeatureFlag>> GetAllAsync()
        {
            throw new NotSupportedException();
        }

        public ValueTask AddAsync(FeatureFlag flag)
        {
            throw new NotSupportedException();
        }

        public ValueTask UpdateAsync(FeatureFlag flag)
        {
            throw new NotSupportedException();
        }

        public ValueTask<bool> DeleteAsync(string key)
        {
            throw new NotSupportedException();
        }
    }
}
