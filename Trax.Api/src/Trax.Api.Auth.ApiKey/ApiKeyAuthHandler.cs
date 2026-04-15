using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Trax.Api.Auth.ApiKey;

/// <summary>
/// ASP.NET Core authentication handler for the Trax API-key scheme. Reads the
/// configured header, delegates validation to an <see cref="ITraxPrincipalResolver{String}"/>,
/// and emits a <see cref="System.Security.Claims.ClaimsPrincipal"/> built from the
/// resolved <see cref="TraxPrincipal"/>.
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// <para>
/// Return semantics:
/// <list type="bullet">
/// <item>Missing header: <see cref="AuthenticateResult.NoResult"/>. Lets other schemes run and <c>[AllowAnonymous]</c> routes stay open.</item>
/// <item>Header present more than once: <see cref="AuthenticateResult.Fail(string)"/>. Ambiguous credentials are refused without invoking the resolver.</item>
/// <item>Present but invalid (resolver returns <c>null</c> or throws): <see cref="AuthenticateResult.Fail(string)"/>. Short-circuits the pipeline.</item>
/// <item>Valid: <see cref="AuthenticateResult.Success"/> with the mapped principal.</item>
/// </list>
/// </para>
/// </remarks>
public sealed class ApiKeyAuthHandler(
    IOptionsMonitor<ApiKeyAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ITraxPrincipalResolver<string> resolver
) : AuthenticationHandler<ApiKeyAuthenticationOptions>(options, logger, encoder)
{
    /// <inheritdoc />
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Options.HeaderName, out var headerValues))
            return AuthenticateResult.NoResult();

        if (headerValues.Count > 1)
            return AuthenticateResult.Fail("Multiple API keys presented.");

        var apiKey = headerValues.ToString();
        if (string.IsNullOrWhiteSpace(apiKey))
            return AuthenticateResult.NoResult();

        // Some reverse proxies coalesce duplicate headers into a single comma-joined
        // value (RFC 7230 §3.2.2). That produces a single StringValues entry that
        // slips past the Count > 1 check above. Reject any value containing a comma
        // so ambiguous credentials are never handed to the resolver.
        if (apiKey.Contains(','))
            return AuthenticateResult.Fail("Ambiguous API key header.");

        TraxPrincipal? principal;
        try
        {
            principal = await resolver.ResolveAsync(apiKey, Context.RequestAborted);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Trax API-key resolver threw an exception.");
            return AuthenticateResult.Fail("API-key resolver failed.");
        }

        if (principal is null)
            return AuthenticateResult.Fail("Invalid API key.");

        var claimsPrincipal = principal.ToClaimsPrincipal(Scheme.Name);
        var ticket = new AuthenticationTicket(claimsPrincipal, Scheme.Name);
        return AuthenticateResult.Success(ticket);
    }
}
