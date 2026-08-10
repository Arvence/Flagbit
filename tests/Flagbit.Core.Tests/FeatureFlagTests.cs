using Flagbit.Core;

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
