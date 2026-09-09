using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Flagbit.Api.Authentication;

public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-Api-Key";

    private readonly ApiKeyOptions _apiKeys;

    public ApiKeyAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, IOptions<ApiKeyOptions> apiKeys) : base(options, logger, encoder)
    {
        _apiKeys = apiKeys.Value;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var values))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (values.Count != 1 || string.IsNullOrWhiteSpace(values[0]))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }

        var providedKey = Encoding.UTF8.GetBytes(values[0]!);
        string role;

        if (Matches(providedKey, _apiKeys.ManagementKey))
        {
            role = ApiKeyOptions.ManagementPolicy;
        }
        else if (Matches(providedKey, _apiKeys.EvaluationKey))
        {
            role = ApiKeyOptions.EvaluationPolicy;
        }
        else
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = SchemeName;
        return base.HandleChallengeAsync(properties);
    }

    private static bool Matches(byte[] providedKey, string configuredKey)
    {
        return CryptographicOperations.FixedTimeEquals(providedKey, Encoding.UTF8.GetBytes(configuredKey));
    }
}
