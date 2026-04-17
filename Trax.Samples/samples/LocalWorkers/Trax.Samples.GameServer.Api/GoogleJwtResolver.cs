using Trax.Api.Auth;
using Trax.Api.Auth.Jwt;
using Trax.Samples.GameServer.Auth;

namespace Trax.Samples.GameServer.Api;

/// <summary>
/// Sample-only resolver that grants every authenticated Google user the
/// <c>Player</c> role so the GameServer sample trains can be exercised by
/// anyone who can sign in. <b>You almost certainly don't need a class like
/// this in a real app.</b>
/// </summary>
/// <remarks>
/// <para>
/// <b>When you don't need a custom resolver.</b> For most apps, the one-line
/// call <c>services.AddTraxJwtAuth(authority, audience)</c> is the whole
/// integration. Trax's <c>DefaultJwtPrincipalResolver</c> already maps the
/// standard OIDC claims (<c>sub</c>, <c>name</c>, <c>preferred_username</c>,
/// <c>email</c>, <c>role</c>/<c>roles</c>) onto a <c>TraxPrincipal</c>. Any
/// provider that emits standard claims works without additional code.
/// </para>
/// <para>
/// <b>When a custom resolver IS justified.</b> Reach for this only when one
/// of the following is true:
/// <list type="bullet">
/// <item>Roles live in a non-standard claim the default mapper doesn't
/// recognize (e.g. tenant-specific Entra app roles or Okta group URIs).</item>
/// <item>You need to enrich the principal from your own database — fetch
/// the user's tenant, permissions, feature flags, etc.</item>
/// <item>You need to reject unknown subjects: allow-list of provisioned
/// users, revocation cache, suspension check.</item>
/// </list>
/// In those cases the pattern is exactly what's below: implement
/// <c>ITraxPrincipalResolver&lt;JwtTokenInput&gt;</c>, read claims off
/// <c>input.Principal</c> (already signature/issuer/audience/lifetime
/// validated by <c>JwtBearerHandler</c>), and return either a
/// <c>TraxPrincipal</c> or <c>null</c> to reject.
/// </para>
/// <para>
/// <b>This sample uses a custom resolver PURELY for the role-assignment
/// hack</b> — hardcoded <c>Player</c> so the trains work out of the box.
/// Remove the generic overload in Program.cs and the default resolver
/// will do the rest.
/// </para>
/// </remarks>
internal sealed class GoogleJwtResolver : ITraxPrincipalResolver<JwtTokenInput>
{
    public ValueTask<TraxPrincipal?> ResolveAsync(JwtTokenInput input, CancellationToken ct)
    {
        var sub = input.Principal.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(sub))
            return new ValueTask<TraxPrincipal?>((TraxPrincipal?)null);

        var name =
            input.Principal.FindFirst("name")?.Value
            ?? input.Principal.FindFirst("email")?.Value
            ?? sub;

        return new ValueTask<TraxPrincipal?>(
            new TraxPrincipal(
                Id: sub,
                DisplayName: name,
                Roles: [nameof(GameRole.Player)],
                PrincipalType: JwtDefaults.PrincipalType
            )
        );
    }
}
