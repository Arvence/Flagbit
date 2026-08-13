using Flagbit.Core.Models;

namespace Flagbit.Core.Tests;

public sealed class FeatureFlagTests
{
    [Fact]
    public void ConstructorCreatesFlagWithGivenState()
    {
        var flag = new FeatureFlag("new-checkout", true);

        Assert.Equal("new-checkout", flag.Key);
        Assert.True(flag.IsEnabled);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ConstructorRejectsMissingKey(string? key)
    {
        Assert.ThrowsAny<ArgumentException>(() => new FeatureFlag(key!, true));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void ConstructorRejectsInvalidRolloutPercentage(int percentage)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FeatureFlag("new-checkout", true, rolloutPercentage: percentage));
    }

    [Fact]
    public void ConfigureEvaluationReplacesSettings()
    {
        var flag = new FeatureFlag("new-checkout", true, ["user-123"], 30);

        flag.ConfigureEvaluation(["user-456", "USER-456", ""], 50);

        Assert.Equal(["user-456"], flag.TargetedUserIds);
        Assert.Equal(50, flag.RolloutPercentage);
    }

    [Fact]
    public void EnableSetsFlagToEnabled()
    {
        var flag = new FeatureFlag("new-checkout", false);

        flag.Enable();

        Assert.True(flag.IsEnabled);
    }

    [Fact]
    public void DisableSetsFlagToDisabled()
    {
        var flag = new FeatureFlag("new-checkout", true);

        flag.Disable();

        Assert.False(flag.IsEnabled);
    }

    [Fact]
    public void StateChangesAreIdempotent()
    {
        var flag = new FeatureFlag("new-checkout", false);

        flag.Enable();
        flag.Enable();
        flag.Disable();
        flag.Disable();

        Assert.False(flag.IsEnabled);
    }

    [Fact]
    public void StateChangesDoNotChangeKey()
    {
        var flag = new FeatureFlag("new-checkout", false);

        flag.Enable();
        flag.Disable();

        Assert.Equal("new-checkout", flag.Key);
    }
}
