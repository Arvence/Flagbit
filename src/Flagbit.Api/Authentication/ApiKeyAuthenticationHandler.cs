using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Flagbit.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Flagbit.Api.Authentication;

public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-Api-Key";

    private readonly ApiKeyOptions _apiKeys;
    private readonly EvaluationApiKeyStore _keyStore;

    public ApiKeyAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, IOptions<ApiKeyOptions> apiKeys, EvaluationApiKeyStore keyStore) : base(options, logger, encoder)
    {
        _apiKeys = apiKeys.Value;
        _keyStore = keyStore;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var values))
        {
            return AuthenticateResult.NoResult();
        }

        if (values.Count != 1 || string.IsNullOrWhiteSpace(values[0]))
        {
            return AuthenticateResult.Fail("Invalid API key.");
        }

        var secret = values[0]!;
        var providedKey = Encoding.UTF8.GetBytes(secret);
        string role;
        Guid? keyId = null;

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
            if (EvaluationApiKeySecret.HasValidFormat(secret))
            {
                keyId = await _keyStore.FindIdByHashAsync(EvaluationApiKeySecret.Hash(secret), Context.RequestAborted);
            }

            if (keyId is null)
            {
                return AuthenticateResult.Fail("Invalid API key.");
            }

            role = ApiKeyOptions.EvaluationPolicy;
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], Scheme.Name);

        if (keyId is not null)
        {
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, keyId.Value.ToString()));
        }

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return AuthenticateResult.Success(ticket);
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
