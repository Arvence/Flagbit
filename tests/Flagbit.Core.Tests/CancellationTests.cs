using Flagbit.Core.Abstractions;
using Flagbit.Core.Models;
using Flagbit.Core.Services;

namespace Flagbit.Core.Tests;

public sealed class CancellationTests
{
    [Theory]
    [InlineData("create")]
    [InlineData("list")]
    [InlineData("get")]
    [InlineData("enable")]
    [InlineData("disable")]
    [InlineData("evaluation")]
    [InlineData("delete")]
    public async Task ManagerForwardsCancellationToEveryStoreOperation(string operation)
    {
        using var cancellation = new CancellationTokenSource();
        var store = new RecordingStore(cancellation.Token);
        var manager = new FeatureFlagManager(store);
        await InvokeAsync(manager, operation, cancellation.Token);
        Assert.NotEmpty(store.Tokens);
        Assert.All(store.Tokens, token => Assert.Equal(cancellation.Token, token));
    }

    [Theory]
    [InlineData("create")]
    [InlineData("list")]
    [InlineData("get")]
    [InlineData("enable")]
    [InlineData("disable")]
    [InlineData("evaluation")]
    [InlineData("delete")]
    public async Task CancelledManagerOperationsNeverReachTheStore(string operation)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var store = new RecordingStore(cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => InvokeAsync(new FeatureFlagManager(store), operation, cancellation.Token));

        Assert.Empty(store.Tokens);
    }

    [Fact]
    public async Task EvaluatorPassesCancellationThroughNestedDependencies()
    {
        using var cancellation = new CancellationTokenSource();
        var store = new RecordingStore(cancellation.Token);
        Assert.True(await new FeatureFlagEvaluator(store).IsEnabledAsync("parent", cancellation.Token));
        Assert.Equal(["parent", "child", "checkout"], store.Keys);
        Assert.All(store.Tokens, token => Assert.Equal(cancellation.Token, token));
    }

    [Fact]
    public async Task CancellationBetweenReadsStopsDependencyTraversal()
    {
        using var cancellation = new CancellationTokenSource();
        var store = new RecordingStore(cancellation.Token)
        {
            AfterRead = key =>
            {
                if (key == "child")
                {
                    cancellation.Cancel();
                }
            }
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await new FeatureFlagEvaluator(store).IsEnabledAsync("parent", new FeatureFlagContext(), cancellation.Token));

        Assert.Equal(["parent", "child"], store.Keys);
    }

    [Fact]
    public async Task CancelledEvaluationNeverReadsAFlag()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var store = new RecordingStore(cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await new FeatureFlagEvaluator(store).IsEnabledAsync("checkout", cancellation.Token));

        Assert.Empty(store.Keys);
    }

    private static async Task InvokeAsync(FeatureFlagManager manager, string operation, CancellationToken cancellationToken)
    {
        switch (operation)
        {
            case "create":
                await manager.CreateAsync("new", cancellationToken);
                break;
            case "list":
                await manager.GetAllAsync(cancellationToken);
                break;
            case "get":
                await manager.GetByKeyAsync("checkout", cancellationToken);
                break;
            case "enable":
                await manager.EnableAsync("checkout", cancellationToken);
                break;
            case "disable":
                await manager.DisableAsync("checkout", cancellationToken);
                break;
            case "evaluation":
                await manager.UpdateEvaluationAsync("checkout", null, null, cancellationToken: cancellationToken);
                break;
            case "delete":
                await manager.DeleteAsync("checkout", cancellationToken);
                break;
            default:
                throw new ArgumentException("Unknown operation.", nameof(operation));
        }
    }

    private sealed class RecordingStore : IFeatureFlagStore
    {
        private readonly CancellationToken _expected;

        public RecordingStore(CancellationToken expected)
        {
            _expected = expected;
        }

        public List<CancellationToken> Tokens { get; } = [];
        public List<string> Keys { get; } = [];
        public Action<string>? AfterRead { get; init; }

        public ValueTask<FeatureFlag?> GetByKeyAsync(string key, CancellationToken cancellationToken = default)
        {
            Record(cancellationToken);
            Keys.Add(key);
            var flag = key == "new" ? null : new FeatureFlag(key, true, dependencyKeys: key switch { "parent" => ["child"], "child" => ["checkout"], _ => [] });
            AfterRead?.Invoke(key);
            return ValueTask.FromResult(flag);
        }

        public ValueTask<IReadOnlyCollection<FeatureFlag>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            Record(cancellationToken);
            return ValueTask.FromResult<IReadOnlyCollection<FeatureFlag>>([]);
        }

        public ValueTask AddAsync(FeatureFlag flag, CancellationToken cancellationToken = default)
        {
            Record(cancellationToken);
            return ValueTask.CompletedTask;
        }

        public ValueTask<FeatureFlag> SetEnabledAsync(string key, bool isEnabled, CancellationToken cancellationToken = default)
        {
            Record(cancellationToken);
            return ValueTask.FromResult(new FeatureFlag(key, isEnabled));
        }

        public ValueTask<FeatureFlag> UpdateEvaluationAsync(FeatureFlag flag, CancellationToken cancellationToken = default)
        {
            Record(cancellationToken);
            return ValueTask.FromResult(flag);
        }

        public ValueTask<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            Record(cancellationToken);
            return ValueTask.FromResult(true);
        }

        private void Record(CancellationToken token)
        {
            Assert.Equal(_expected, token);
            token.ThrowIfCancellationRequested();
            Tokens.Add(token);
        }
    }
}
