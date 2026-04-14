using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Trax.Samples.JobHunt.Auth;

/// <summary>
/// Maps X-Api-Key headers to JobHunt user identities.
/// Three users: alice, bob, charlie. For demonstration only.
/// </summary>
public class ApiKeyAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder
) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private static readonly Dictionary<string, (string UserId, string DisplayName)> Users = new()
    {
        [ApiKeyDefaults.AliceKey] = ("alice", "Alice"),
        [ApiKeyDefaults.BobKey] = ("bob", "Bob"),
        [ApiKeyDefaults.CharlieKey] = ("charlie", "Charlie"),
    };

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyDefaults.HeaderName, out var headerValue))
            return Task.FromResult(
                AuthenticateResult.Fail($"Missing {ApiKeyDefaults.HeaderName} header")
            );

        var apiKey = headerValue.ToString();

        if (!Users.TryGetValue(apiKey, out var user))
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key"));

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId),
            new(ClaimTypes.Name, user.DisplayName),
            new(ClaimTypes.Role, "User"),
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
