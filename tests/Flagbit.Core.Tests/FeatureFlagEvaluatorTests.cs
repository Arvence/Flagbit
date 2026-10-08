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
    [InlineData(0, "user-123", false)]
    [InlineData(100, "user-123", true)]
    [InlineData(100, null, false)]
    [InlineData(100, "", false)]
    [InlineData(100, "   ", false)]
    public async Task IsEnabledAsyncAppliesPercentageBoundaries(int percentage, string? userId, bool expected)
    {
        var flag = new FeatureFlag("new-checkout", true, rolloutPercentage: percentage);
        var evaluator = new FeatureFlagEvaluator(new StubFeatureFlagStore(flag));

        var isEnabled = await evaluator.IsEnabledAsync("new-checkout", new FeatureFlagContext(UserId: userId));

        Assert.Equal(expected, isEnabled);
    }

    [Theory]
    [InlineData("new-checkout", "user-123", 44)]
    [InlineData("new-checkout", "USER-123", 84)]
    [InlineData("new-checkout", " user-123 ", 55)]
    [InlineData("NEW-CHECKOUT", "user-123", 77)]
    [InlineData("new-checkout", "user-456", 5)]
    [InlineData("caf\u00e9", "\u6771\u4eac", 21)]
    public async Task IsEnabledAsyncPreservesKnownRolloutAssignments(string storedKey, string userId, int expectedBucket)
    {
        for (var instance = 0; instance < 2; instance++)
        {
            var flag = new FeatureFlag(storedKey, true, rolloutPercentage: expectedBucket);
            var evaluator = new FeatureFlagEvaluator(new StubFeatureFlagStore(flag));
            var context = new FeatureFlagContext(UserId: userId);

            for (var request = 0; request < 2; request++)
            {
                Assert.False(await evaluator.IsEnabledAsync(storedKey, context));
                Assert.False(await evaluator.IsEnabledAsync(storedKey.ToUpperInvariant(), context));
            }

            flag.ConfigureEvaluation(null, expectedBucket + 1);

            Assert.True(await evaluator.IsEnabledAsync(storedKey, context));
            Assert.True(await evaluator.IsEnabledAsync(storedKey.ToUpperInvariant(), context));
        }
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
    [InlineData("enabled", false)]
    [InlineData("enabled", true)]
    [InlineData("disabled", false)]
    [InlineData("disabled", true)]
    [InlineData("missing", false)]
    [InlineData("missing", true)]
    public async Task IsEnabledAsyncRejectsAmbiguousAttributesBeforeEvaluatingFlag(string key, bool reverseOrder)
    {
        var evaluator = new FeatureFlagEvaluator(new StubFeatureFlagStore(new FeatureFlag("enabled", true), new FeatureFlag("disabled", false)));
        var attributes = reverseOrder
            ? new Dictionary<string, string> { ["PLAN"] = "free", ["plan"] = "enterprise" }
            : new Dictionary<string, string> { ["plan"] = "enterprise", ["PLAN"] = "free" };

        await Assert.ThrowsAsync<ArgumentException>(async () => await evaluator.IsEnabledAsync(key, new FeatureFlagContext(Attributes: attributes)));
    }

    [Fact]
    public async Task IsEnabledAsyncRevalidatesAttributesChangedAfterContextCreation()
    {
        var evaluator = new FeatureFlagEvaluator(new StubFeatureFlagStore(new FeatureFlag("checkout", true)));
        var attributes = new Dictionary<string, string> { ["plan"] = "enterprise" };
        var context = new FeatureFlagContext(Attributes: attributes);

        Assert.True(await evaluator.IsEnabledAsync("checkout", context));
        attributes.Add("PLAN", "enterprise");

        await Assert.ThrowsAsync<ArgumentException>(async () => await evaluator.IsEnabledAsync("checkout", context));
    }

    [Theory]
    [InlineData(FeatureFlagRuleOperator.Equals, "ENTERPRISE", true)]
    [InlineData(FeatureFlagRuleOperator.NotEquals, "FREE", true)]
    [InlineData(FeatureFlagRuleOperator.Contains, "TERP", true)]
    [InlineData(FeatureFlagRuleOperator.StartsWith, "ENTER", true)]
    [InlineData(FeatureFlagRuleOperator.EndsWith, "PRISE", true)]
    [InlineData(FeatureFlagRuleOperator.Equals, "free", false)]
    [InlineData(FeatureFlagRuleOperator.NotEquals, "ENTERPRISE", false)]
    [InlineData(FeatureFlagRuleOperator.Contains, "premium", false)]
    [InlineData(FeatureFlagRuleOperator.StartsWith, "prise", false)]
    [InlineData(FeatureFlagRuleOperator.EndsWith, "enter", false)]
    public async Task IsEnabledAsyncAppliesRuleOperator(FeatureFlagRuleOperator ruleOperator, string expectedValue, bool expected)
    {
        var flag = new FeatureFlag("new-checkout", true, rules: [new FeatureFlagRule("plan", ruleOperator, expectedValue)]);
        var evaluator = new FeatureFlagEvaluator(new StubFeatureFlagStore(flag));
        var context = new FeatureFlagContext(Attributes: new Dictionary<string, string> { ["PLAN"] = "enterprise" });

        var isEnabled = await evaluator.IsEnabledAsync("new-checkout", context);

        Assert.Equal(expected, isEnabled);
    }

    [Theory]
    [InlineData(FeatureFlagRuleOperator.Equals)]
    [InlineData(FeatureFlagRuleOperator.NotEquals)]
    [InlineData(FeatureFlagRuleOperator.Contains)]
    [InlineData(FeatureFlagRuleOperator.StartsWith)]
    [InlineData(FeatureFlagRuleOperator.EndsWith)]
    public async Task IsEnabledAsyncFailsEveryOperatorForMissingOrNullAttributes(FeatureFlagRuleOperator ruleOperator)
    {
        var flag = new FeatureFlag("checkout", true, rules: [new FeatureFlagRule("plan", ruleOperator, "enterprise")]);
        var evaluator = new FeatureFlagEvaluator(new StubFeatureFlagStore(flag));

        Assert.False(await evaluator.IsEnabledAsync("checkout", FeatureFlagContext.Empty));
        Assert.False(await evaluator.IsEnabledAsync("checkout", new FeatureFlagContext(Attributes: new Dictionary<string, string>())));
        Assert.False(await evaluator.IsEnabledAsync("checkout", new FeatureFlagContext(Attributes: new Dictionary<string, string> { ["other"] = "enterprise" })));
        Assert.False(await evaluator.IsEnabledAsync("checkout", new FeatureFlagContext(Attributes: new Dictionary<string, string> { ["PLAN"] = null! })));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("state")]
    [InlineData("targeting")]
    [InlineData("rollout")]
    [InlineData("environment")]
    [InlineData("rule")]
    [InlineData("start")]
    [InlineData("end")]
    [InlineData("dependency")]
    public async Task IsEnabledAsyncRequiresEveryRestrictionEvenWhenOthersMatch(string? failingRestriction)
    {
        var now = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        var dependency = new FeatureFlag("accounts", failingRestriction != "dependency");
        var flag = new FeatureFlag("new-checkout", failingRestriction != "state", ["USER-123"], failingRestriction == "rollout" ? 44 : 50,
            ["PRODUCTION"], [new FeatureFlagRule("plan", FeatureFlagRuleOperator.Equals, "enterprise")],
            failingRestriction == "start" ? now.AddTicks(1) : now.AddHours(-1),
            failingRestriction == "end" ? now.AddTicks(-1) : now.AddHours(1), ["ACCOUNTS"]);
        var context = new FeatureFlagContext(failingRestriction == "targeting" ? "user-456" : "user-123",
            failingRestriction == "environment" ? "staging" : "production", now,
            new Dictionary<string, string> { ["PLAN"] = failingRestriction == "rule" ? "free" : "Enterprise" });
        var evaluator = new FeatureFlagEvaluator(new StubFeatureFlagStore(flag, dependency));

        Assert.Equal(failingRestriction is null, await evaluator.IsEnabledAsync("NEW-CHECKOUT", context));
    }

    [Fact]
    public async Task IsEnabledAsyncDoesNotLetTargetingBypassZeroRollout()
    {
        var flag = new FeatureFlag("checkout", true, ["user-123"], 0);
        var evaluator = new FeatureFlagEvaluator(new StubFeatureFlagStore(flag));

        Assert.False(await evaluator.IsEnabledAsync("checkout", new FeatureFlagContext(UserId: "USER-123")));
    }

    [Theory]
    [InlineData(" user ", " production ", true)]
    [InlineData(" USER ", " PRODUCTION ", true)]
    [InlineData("user", " production ", false)]
    [InlineData(" user ", "production", false)]
    public async Task IsEnabledAsyncPreservesTargetAndEnvironmentWhitespace(string userId, string environment, bool expected)
    {
        var flag = new FeatureFlag("checkout", true, [" user "], environments: [" production "]);
        var evaluator = new FeatureFlagEvaluator(new StubFeatureFlagStore(flag));

        Assert.Equal(expected, await evaluator.IsEnabledAsync("checkout", new FeatureFlagContext(userId, environment)));
    }

    [Theory]
    [InlineData("plan", " enterprise ", true)]
    [InlineData("PLAN", " ENTERPRISE ", true)]
    [InlineData(" plan ", " enterprise ", false)]
    [InlineData("plan", "enterprise", false)]
    public async Task IsEnabledAsyncTrimsOnlyConfiguredRuleAttributeNames(string attribute, string value, bool expected)
    {
        var flag = new FeatureFlag("checkout", true, rules: [new FeatureFlagRule(" plan ", FeatureFlagRuleOperator.Equals, " enterprise ")]);
        var evaluator = new FeatureFlagEvaluator(new StubFeatureFlagStore(flag));
        var context = new FeatureFlagContext(Attributes: new Dictionary<string, string> { [attribute] = value });

        Assert.Equal(expected, await evaluator.IsEnabledAsync("checkout", context));
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

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public async Task IsEnabledAsyncMatchesEqualScheduleInstantsAcrossOffsets(long ticksFromBoundary, bool expected)
    {
        var boundary = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.FromHours(3));
        var flag = new FeatureFlag("scheduled", true, startsAt: boundary, endsAt: boundary.ToUniversalTime());
        var evaluator = new FeatureFlagEvaluator(new StubFeatureFlagStore(flag));
        var context = new FeatureFlagContext(CurrentTime: boundary.ToOffset(TimeSpan.FromHours(-4)).AddTicks(ticksFromBoundary));

        Assert.Equal(expected, await evaluator.IsEnabledAsync("scheduled", context));
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
        var now = new DateTimeOffset(2026, 8, 14, 9, 0, 0, TimeSpan.Zero);
        var sharedDependency = new FeatureFlag("identity", true, ["USER-123"], 100, ["production"],
            [new FeatureFlagRule("plan", FeatureFlagRuleOperator.Equals, "enterprise")], now, now);
        var firstDependency = new FeatureFlag("accounts", true, startsAt: now, endsAt: now, dependencyKeys: ["IDENTITY"]);
        var secondDependency = new FeatureFlag("recommendations", true, dependencyKeys: ["Identity"]);
        var flag = new FeatureFlag("new-checkout", true, startsAt: now, endsAt: now, dependencyKeys: ["ACCOUNTS", "Recommendations"]);
        var evaluator = new FeatureFlagEvaluator(new StubFeatureFlagStore(flag, firstDependency, secondDependency, sharedDependency));
        var context = new FeatureFlagContext("user-123", "PRODUCTION", now, new Dictionary<string, string> { ["PLAN"] = "Enterprise" });

        Assert.True(await evaluator.IsEnabledAsync("NEW-CHECKOUT", context));
        Assert.False(await evaluator.IsEnabledAsync("new-checkout", context with { UserId = null }));
        Assert.False(await evaluator.IsEnabledAsync("new-checkout", context with { Environment = "staging" }));
        Assert.False(await evaluator.IsEnabledAsync("new-checkout", context with { Attributes = null }));
        Assert.False(await evaluator.IsEnabledAsync("new-checkout", context with { CurrentTime = now.AddTicks(1) }));
    }

    [Fact]
    public async Task IsEnabledAsyncReturnsFalseForDependencyCycle()
    {
        var first = new FeatureFlag("first", true, dependencyKeys: ["SECOND"]);
        var second = new FeatureFlag("second", true, dependencyKeys: ["Third"]);
        var third = new FeatureFlag("third", true, dependencyKeys: ["FIRST"]);
        var evaluator = new FeatureFlagEvaluator(new StubFeatureFlagStore(first, second, third));

        Assert.False(await evaluator.IsEnabledAsync("first"));
        Assert.False(await evaluator.IsEnabledAsync("SECOND"));
    }

    [Fact]
    public async Task IsEnabledAsyncReturnsFalseForMissingNestedDependency()
    {
        var first = new FeatureFlag("first", true, dependencyKeys: ["SECOND"]);
        var second = new FeatureFlag("second", true, dependencyKeys: ["missing"]);
        var evaluator = new FeatureFlagEvaluator(new StubFeatureFlagStore(first, second));

        Assert.False(await evaluator.IsEnabledAsync("first"));
    }

    [Fact]
    public async Task IsEnabledAsyncPreservesWhitespaceInKeysAndDependencies()
    {
        var dependency = new FeatureFlag(" dependency ", true);
        var flag = new FeatureFlag(" checkout ", true, dependencyKeys: [" DEPENDENCY "]);
        var evaluator = new FeatureFlagEvaluator(new StubFeatureFlagStore(flag, dependency));

        Assert.True(await evaluator.IsEnabledAsync(" CHECKOUT "));
        Assert.False(await evaluator.IsEnabledAsync("checkout"));
        flag.ConfigureEvaluation(null, null, dependencyKeys: ["dependency"]);
        Assert.False(await evaluator.IsEnabledAsync(" checkout "));
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

        public ValueTask<FeatureFlag> SetEnabledAsync(string key, bool isEnabled)
        {
            throw new NotSupportedException();
        }

        public ValueTask<FeatureFlag> UpdateEvaluationAsync(FeatureFlag flag)
        {
            throw new NotSupportedException();
        }

        public ValueTask<bool> DeleteAsync(string key)
        {
            throw new NotSupportedException();
        }
    }
}
