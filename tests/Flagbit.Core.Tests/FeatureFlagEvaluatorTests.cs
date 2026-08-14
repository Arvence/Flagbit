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
        var evaluator = new FeatureFlagEvaluator(new StubFeatureFlagStore());

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

    [Theory]
    [InlineData("production", true)]
    [InlineData("PRODUCTION", true)]
    [InlineData("staging", false)]
    [InlineData(null, false)]
    public async Task IsEnabledAsyncMatchesEnvironment(string? environment, bool expected)
    {
        var flag = new FeatureFlag("new-checkout", true, environments: ["production"]);
        var evaluator = new FeatureFlagEvaluator(new StubFeatureFlagStore(flag));

        var isEnabled = await evaluator.IsEnabledAsync("new-checkout", new FeatureFlagContext(Environment: environment));

        Assert.Equal(expected, isEnabled);
    }

    [Fact]
    public async Task IsEnabledAsyncRequiresEveryRuleToMatch()
    {
        var rules = new[]
        {
            new FeatureFlagRule("plan", FeatureFlagRuleOperator.Equals, "enterprise"),
            new FeatureFlagRule("email", FeatureFlagRuleOperator.EndsWith, "@example.com")
        };
        var flag = new FeatureFlag("new-checkout", true, rules: rules);
        var evaluator = new FeatureFlagEvaluator(new StubFeatureFlagStore(flag));
        var matchingAttributes = new Dictionary<string, string> { ["PLAN"] = "Enterprise", ["email"] = "user@example.com" };
        var failingAttributes = new Dictionary<string, string> { ["plan"] = "free", ["email"] = "user@example.com" };

        Assert.True(await evaluator.IsEnabledAsync("new-checkout", new FeatureFlagContext(Attributes: matchingAttributes)));
        Assert.False(await evaluator.IsEnabledAsync("new-checkout", new FeatureFlagContext(Attributes: failingAttributes)));
        Assert.False(await evaluator.IsEnabledAsync("new-checkout", FeatureFlagContext.Empty));
    }

    [Theory]
    [InlineData(FeatureFlagRuleOperator.Equals, "enterprise", true)]
    [InlineData(FeatureFlagRuleOperator.NotEquals, "free", true)]
    [InlineData(FeatureFlagRuleOperator.Contains, "terp", true)]
    [InlineData(FeatureFlagRuleOperator.StartsWith, "enter", true)]
    [InlineData(FeatureFlagRuleOperator.EndsWith, "prise", true)]
    [InlineData(FeatureFlagRuleOperator.Equals, "free", false)]
    public async Task IsEnabledAsyncAppliesRuleOperator(FeatureFlagRuleOperator ruleOperator, string expectedValue, bool expected)
    {
        var flag = new FeatureFlag("new-checkout", true, rules: [new FeatureFlagRule("plan", ruleOperator, expectedValue)]);
        var evaluator = new FeatureFlagEvaluator(new StubFeatureFlagStore(flag));
        var context = new FeatureFlagContext(Attributes: new Dictionary<string, string> { ["plan"] = "enterprise" });

        var isEnabled = await evaluator.IsEnabledAsync("new-checkout", context);

        Assert.Equal(expected, isEnabled);
    }

    [Fact]
    public async Task IsEnabledAsyncMatchesScheduleInclusively()
    {
        var startsAt = new DateTimeOffset(2026, 8, 14, 9, 0, 0, TimeSpan.Zero);
        var endsAt = startsAt.AddHours(2);
        var flag = new FeatureFlag("new-checkout", true, startsAt: startsAt, endsAt: endsAt);
        var evaluator = new FeatureFlagEvaluator(new StubFeatureFlagStore(flag));

        Assert.False(await evaluator.IsEnabledAsync("new-checkout", new FeatureFlagContext(CurrentTime: startsAt.AddTicks(-1))));
        Assert.True(await evaluator.IsEnabledAsync("new-checkout", new FeatureFlagContext(CurrentTime: startsAt)));
        Assert.True(await evaluator.IsEnabledAsync("new-checkout", new FeatureFlagContext(CurrentTime: endsAt)));
        Assert.False(await evaluator.IsEnabledAsync("new-checkout", new FeatureFlagContext(CurrentTime: endsAt.AddTicks(1))));
    }

    [Fact]
    public async Task IsEnabledAsyncMatchesOpenEndedSchedules()
    {
        var boundary = new DateTimeOffset(2026, 8, 14, 9, 0, 0, TimeSpan.Zero);
        var startsAtFlag = new FeatureFlag("starts-at", true, startsAt: boundary);
        var endsAtFlag = new FeatureFlag("ends-at", true, endsAt: boundary);
        var evaluator = new FeatureFlagEvaluator(new StubFeatureFlagStore(startsAtFlag, endsAtFlag));

        Assert.False(await evaluator.IsEnabledAsync("starts-at", new FeatureFlagContext(CurrentTime: boundary.AddTicks(-1))));
        Assert.True(await evaluator.IsEnabledAsync("starts-at", new FeatureFlagContext(CurrentTime: boundary)));
        Assert.True(await evaluator.IsEnabledAsync("ends-at", new FeatureFlagContext(CurrentTime: boundary)));
        Assert.False(await evaluator.IsEnabledAsync("ends-at", new FeatureFlagContext(CurrentTime: boundary.AddTicks(1))));
    }

    [Fact]
    public async Task IsEnabledAsyncRequiresEveryDependencyToBeEnabled()
    {
        var firstDependency = new FeatureFlag("accounts", true);
        var secondDependency = new FeatureFlag("recommendations", false);
        var flag = new FeatureFlag("new-checkout", true, dependencyKeys: [firstDependency.Key, secondDependency.Key]);
        var evaluator = new FeatureFlagEvaluator(new StubFeatureFlagStore(flag, firstDependency, secondDependency));

        Assert.False(await evaluator.IsEnabledAsync("new-checkout"));

        secondDependency.Enable();

        Assert.True(await evaluator.IsEnabledAsync("new-checkout"));
    }

    [Fact]
    public async Task IsEnabledAsyncEvaluatesNestedDependenciesWithTheSameContext()
    {
        var sharedDependency = new FeatureFlag("identity", true, environments: ["production"]);
        var firstDependency = new FeatureFlag("accounts", true, dependencyKeys: [sharedDependency.Key]);
        var secondDependency = new FeatureFlag("recommendations", true, dependencyKeys: [sharedDependency.Key]);
        var flag = new FeatureFlag("new-checkout", true, dependencyKeys: [firstDependency.Key, secondDependency.Key]);
        var evaluator = new FeatureFlagEvaluator(new StubFeatureFlagStore(flag, firstDependency, secondDependency, sharedDependency));

        Assert.True(await evaluator.IsEnabledAsync("new-checkout", new FeatureFlagContext(Environment: "production")));
        Assert.False(await evaluator.IsEnabledAsync("new-checkout", new FeatureFlagContext(Environment: "staging")));
    }

    [Fact]
    public async Task IsEnabledAsyncReturnsFalseForDependencyCycle()
    {
        var first = new FeatureFlag("first", true, dependencyKeys: ["second"]);
        var second = new FeatureFlag("second", true, dependencyKeys: ["first"]);
        var evaluator = new FeatureFlagEvaluator(new StubFeatureFlagStore(first, second));

        Assert.False(await evaluator.IsEnabledAsync("first"));
    }

    [Fact]
    public async Task IsEnabledAsyncRejectsMissingKey()
    {
        var evaluator = new FeatureFlagEvaluator(new StubFeatureFlagStore());

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await evaluator.IsEnabledAsync(" "));
    }

    [Fact]
    public void ConstructorRejectsMissingStore()
    {
        Assert.Throws<ArgumentNullException>(() => new FeatureFlagEvaluator(null!));
    }

    private sealed class StubFeatureFlagStore : IFeatureFlagStore
    {
        private readonly IReadOnlyDictionary<string, FeatureFlag> _flags;

        public StubFeatureFlagStore(params FeatureFlag[] flags)
        {
            _flags = flags.ToDictionary(flag => flag.Key, StringComparer.OrdinalIgnoreCase);
        }

        public ValueTask<FeatureFlag?> GetByKeyAsync(string key)
        {
            _flags.TryGetValue(key, out var flag);
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
