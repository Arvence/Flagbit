using Flagbit.Core;

namespace Flagbit.Core.Tests;

public sealed class FeatureFlagManagerTests
{
    [Fact]
    public async Task CreateAsyncCreatesDisabledFlagByDefault()
    {
        var store = new InMemoryFeatureFlagStore();
        var manager = new FeatureFlagManager(store);

        var flag = await manager.CreateAsync("new-checkout");

        Assert.Equal("new-checkout", flag.Key);
        Assert.False(flag.IsEnabled);
        Assert.Same(flag, await store.GetByKeyAsync("new-checkout"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CreateAsyncUsesRequestedInitialState(bool isEnabled)
    {
        var manager = new FeatureFlagManager(new InMemoryFeatureFlagStore());

        var flag = await manager.CreateAsync("new-checkout", isEnabled);

        Assert.Equal(isEnabled, flag.IsEnabled);
    }

    [Fact]
    public async Task CreateAsyncRejectsDuplicateKey()
    {
        var store = new InMemoryFeatureFlagStore(
            new FeatureFlag("new-checkout", false));
        var manager = new FeatureFlagManager(store);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await manager.CreateAsync("new-checkout"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsyncRejectsMissingKey(string? key)
    {
        var manager = new FeatureFlagManager(new InMemoryFeatureFlagStore());

        await Assert.ThrowsAnyAsync<ArgumentException>(
            async () => await manager.CreateAsync(key!));
    }

    [Fact]
    public async Task GetAllAsyncReturnsStoredFlags()
    {
        var first = new FeatureFlag("new-checkout", true);
        var second = new FeatureFlag("recommendations", false);
        var manager = new FeatureFlagManager(
            new InMemoryFeatureFlagStore(first, second));

        var flags = await manager.GetAllAsync();

        Assert.Equal(new[] { first, second }, flags);
    }

    [Fact]
    public async Task EnableAsyncEnablesAndPersistsFlag()
    {
        var flag = new FeatureFlag("new-checkout", false);
        var store = new InMemoryFeatureFlagStore(flag);
        var manager = new FeatureFlagManager(store);

        var result = await manager.EnableAsync("new-checkout");

        Assert.Same(flag, result);
        Assert.True(result.IsEnabled);
        Assert.Same(flag, store.LastUpdatedFlag);
    }

    [Fact]
    public async Task DisableAsyncDisablesAndPersistsFlag()
    {
        var flag = new FeatureFlag("new-checkout", true);
        var store = new InMemoryFeatureFlagStore(flag);
        var manager = new FeatureFlagManager(store);

        var result = await manager.DisableAsync("new-checkout");

        Assert.Same(flag, result);
        Assert.False(result.IsEnabled);
        Assert.Same(flag, store.LastUpdatedFlag);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IsEnabledAsyncReturnsStoredState(bool storedState)
    {
        var manager = new FeatureFlagManager(
            new InMemoryFeatureFlagStore(
                new FeatureFlag("new-checkout", storedState)));

        var isEnabled = await manager.IsEnabledAsync("new-checkout");

        Assert.Equal(storedState, isEnabled);
    }

    [Fact]
    public async Task IsEnabledAsyncReturnsFalseForUnknownFlag()
    {
        var manager = new FeatureFlagManager(new InMemoryFeatureFlagStore());

        var isEnabled = await manager.IsEnabledAsync("unknown-feature");

        Assert.False(isEnabled);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StateChangeThrowsWhenFlagDoesNotExist(bool enable)
    {
        var manager = new FeatureFlagManager(new InMemoryFeatureFlagStore());

        async Task ChangeState() => _ = enable
            ? await manager.EnableAsync("unknown-feature")
            : await manager.DisableAsync("unknown-feature");

        await Assert.ThrowsAsync<KeyNotFoundException>(ChangeState);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task StateChangeRejectsMissingKey(string? key)
    {
        var manager = new FeatureFlagManager(new InMemoryFeatureFlagStore());

        await Assert.ThrowsAnyAsync<ArgumentException>(
            async () => await manager.EnableAsync(key!));
        await Assert.ThrowsAnyAsync<ArgumentException>(
            async () => await manager.DisableAsync(key!));
    }

    [Fact]
    public void ConstructorRejectsMissingStore()
    {
        Assert.Throws<ArgumentNullException>(() => new FeatureFlagManager(null!));
    }

    private sealed class InMemoryFeatureFlagStore : IFeatureFlagStore
    {
        private readonly List<FeatureFlag> _flags;

        public InMemoryFeatureFlagStore(params FeatureFlag[] flags)
        {
            _flags = [.. flags];
        }

        public FeatureFlag? LastUpdatedFlag { get; private set; }

        public ValueTask<FeatureFlag?> GetByKeyAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(
                _flags.SingleOrDefault(flag => flag.Key == key));
        }

        public ValueTask<IReadOnlyCollection<FeatureFlag>> GetAllAsync(
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<IReadOnlyCollection<FeatureFlag>>(
                _flags.AsReadOnly());
        }

        public ValueTask AddAsync(
            FeatureFlag flag,
            CancellationToken cancellationToken = default)
        {
            _flags.Add(flag);
            return ValueTask.CompletedTask;
        }

        public ValueTask UpdateAsync(
            FeatureFlag flag,
            CancellationToken cancellationToken = default)
        {
            LastUpdatedFlag = flag;
            return ValueTask.CompletedTask;
        }
    }
}
