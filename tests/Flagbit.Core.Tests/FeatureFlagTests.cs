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
}
