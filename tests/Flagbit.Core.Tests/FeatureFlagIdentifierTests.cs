using Flagbit.Core.Models;

namespace Flagbit.Core.Tests;

public sealed class FeatureFlagIdentifierTests
{
    [Theory]
    [InlineData("checkout", "CHECKOUT")]
    [InlineData("caf\u00e9", "CAF\u00c9")]
    [InlineData("\u0131stanbul", "\u0131STANBUL")]
    [InlineData("\u0130stanbul", "\u0130STANBUL")]
    [InlineData("\u017f", "\u017f")]
    [InlineData("stra\u00dfe", "STRA\u00dfE")]
    [InlineData("\u03c2\u03c3", "\u03a3\u03a3")]
    [InlineData("\U00010428", "\U00010400")]
    [InlineData(" \u017f-caf\u00e9-\U00010428 ", " \u017f-CAF\u00c9-\U00010400 ")]
    public void NormalizationPreservesOrdinalIdentity(string value, string expected)
    {
        var normalized = FeatureFlagIdentifier.Normalize(value);

        Assert.Equal(expected, normalized);
        Assert.True(StringComparer.OrdinalIgnoreCase.Equals(value, normalized));
        Assert.Equal(normalized, FeatureFlagIdentifier.Normalize(normalized));
    }

    [Theory]
    [InlineData("i", "\u0131")]
    [InlineData("S", "\u017f")]
    [InlineData("caf\u00e9", "cafe\u0301")]
    public void OrdinallyDistinctIdentifiersStayDistinct(string first, string second)
    {
        Assert.False(StringComparer.OrdinalIgnoreCase.Equals(first, second));
        Assert.NotEqual(FeatureFlagIdentifier.Normalize(first), FeatureFlagIdentifier.Normalize(second));
    }
}
