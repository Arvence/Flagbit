using System.Text;

namespace Flagbit.Core.Models;

public static class FeatureFlagIdentifier
{
    public static string Normalize(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var upper = value.ToUpperInvariant();
        if (string.Equals(value, upper, StringComparison.OrdinalIgnoreCase))
        {
            return upper;
        }

        var normalized = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var length = char.IsHighSurrogate(value[index]) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]) ? 2 : 1;
            var character = value.Substring(index, length);
            var candidate = character.ToUpperInvariant();
            normalized.Append(string.Equals(character, candidate, StringComparison.OrdinalIgnoreCase) ? candidate : character);
            index += length - 1;
        }

        return normalized.ToString();
    }
}
