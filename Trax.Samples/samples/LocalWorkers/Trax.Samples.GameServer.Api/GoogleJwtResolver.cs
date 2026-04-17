using Trax.Api.Auth;
using Trax.Api.Auth.Jwt;
using Trax.Samples.GameServer.Auth;

namespace Trax.Samples.GameServer.Api;

/// <summary>
/// Maps a Google-issued id-token onto a Trax principal. The JWT bearer
/// handler has already verified signature, issuer, audience, and lifetime
/// against Google's JWKS before this runs.
/// </summary>
/// <remarks>
/// Demo only: every authenticated Google user is granted the Player role so
/// sample trains work out of the box. A real deployment would look up the
/// principal in the game's user table and assign roles based on account
/// state, revoke unknown subjects, etc.
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
