using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Trax.Samples.GameServer.Auth;

/// <summary>
/// A simple API key authentication handler that reads the X-Api-Key header
/// and maps it to a ClaimsPrincipal with appropriate roles.
///
/// Two keys are supported:
///   - AdminKey  → roles: Admin, Player
///   - PlayerKey → role:  Player
///
/// This is for demonstration only. In production, use JWT, OAuth, or another
/// standard authentication mechanism.
/// </summary>
public class ApiKeyAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder
) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyDefaults.HeaderName, out var headerValue))
            return Task.FromResult(
                AuthenticateResult.Fail($"Missing {ApiKeyDefaults.HeaderName} header")
            );

        var apiKey = headerValue.ToString();

        var claims = new List<Claim>();

        if (apiKey == ApiKeyDefaults.AdminKey)
        {
            claims.Add(new Claim(ClaimTypes.Name, "admin"));
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));
            claims.Add(new Claim(ClaimTypes.Role, "Player"));
        }
        else if (apiKey == ApiKeyDefaults.PlayerKey)
        {
            claims.Add(new Claim(ClaimTypes.Name, "player"));
            claims.Add(new Claim(ClaimTypes.Role, "Player"));
        }
        else
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key"));
        }

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
