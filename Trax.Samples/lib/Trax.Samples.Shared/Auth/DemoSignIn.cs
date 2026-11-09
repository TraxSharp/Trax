using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Trax.Api.Auth.Jwt;

namespace Trax.Samples.Shared.Auth;

/// <summary>What a page sends to sign one person in: a name and a role, nothing else.</summary>
public sealed record DemoSignInRequest(string Name, string Role);

/// <summary>
/// Registers and maps a <see cref="DemoTokenIssuer"/>, in Development only (ADR <c>Trax.Docs/adr/0035</c>). Outside
/// Development both calls do nothing: the host has no sign-in and no token endpoint, so a token signed with the demo
/// key reads nothing.
/// </summary>
public static class DemoSignIn
{
    /// <summary>The path a page asks for a token at.</summary>
    public const string DefaultPattern = "/dev/token";

    /// <summary>Accepts the issuer's tokens as the host's JWT sign-in, in Development only.</summary>
    public static WebApplicationBuilder AddDemoSignIn(
        this WebApplicationBuilder builder,
        DemoTokenIssuer issuer
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(issuer);
        if (builder.Environment.IsDevelopment())
            builder.Services.AddTraxJwtAuth(jwt =>
                jwt.UseSymmetricKey(issuer.Issuer, issuer.Audience, issuer.SigningKey)
            );
        return builder;
    }

    /// <summary>
    /// Maps <c>POST {pattern}</c> <c>{ "name": "Ada", "role": "customer" }</c> to <c>{ "token": "…" }</c>, anonymous,
    /// in Development only. A name or role the issuer refuses is a 400.
    /// </summary>
    /// <returns>The endpoint, or <see langword="null"/> outside Development.</returns>
    public static IEndpointConventionBuilder? MapDemoSignIn(
        this WebApplication app,
        DemoTokenIssuer issuer,
        string pattern = DefaultPattern
    )
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(issuer);
        if (!app.Environment.IsDevelopment())
            return null;
        return app.MapPost(
                pattern,
                (DemoSignInRequest request) =>
                {
                    try
                    {
                        return Results.Ok(new { token = issuer.Mint(request.Name, request.Role) });
                    }
                    catch (ArgumentException ex)
                    {
                        return Results.BadRequest(new { error = ex.Message });
                    }
                }
            )
            .AllowAnonymous();
    }
}
