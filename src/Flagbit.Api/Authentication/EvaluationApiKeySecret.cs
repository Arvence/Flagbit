using System.Security.Cryptography;
using System.Text;

namespace Flagbit.Api.Authentication;

internal static class EvaluationApiKeySecret
{
    private const string Prefix = "fb_eval_";

    public static string Generate()
    {
        return Prefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    }

    public static bool HasValidFormat(string key)
    {
        return key.Length == Prefix.Length + 64
            && key.StartsWith(Prefix, StringComparison.Ordinal)
            && key.AsSpan(Prefix.Length).IndexOfAnyExcept("0123456789ABCDEFabcdef") < 0;
    }

    public static string Hash(string key)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    }
}
