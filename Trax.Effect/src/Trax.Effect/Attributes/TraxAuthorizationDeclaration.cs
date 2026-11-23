using System.ComponentModel;
using System.Reflection;

namespace Trax.Effect.Attributes;

/// <summary>
/// The authorization posture a class, interface or method declares, plus anything it declared
/// that Trax refuses to honour.
/// </summary>
/// <param name="Authorize">
/// Every <see cref="TraxAuthorizeAttribute"/> found, de-duplicated by reference identity so a
/// single attribute instance is not counted twice when the CLR returns it through several
/// inheritance paths.
/// </param>
/// <param name="AllowAnonymous">Whether <see cref="TraxAllowAnonymousAttribute"/> is present.</param>
/// <param name="ForeignAttributes">
/// Full names of authorization attributes from another framework found on the same member. Trax
/// does not read them, and a surface carrying one has not declared a posture Trax can enforce.
/// </param>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record TraxAuthorizationDeclaration(
    IReadOnlyList<TraxAuthorizeAttribute> Authorize,
    bool AllowAnonymous,
    IReadOnlyList<string> ForeignAttributes
)
{
    /// <summary>The surface is gated.</summary>
    public bool HasAuthorize => Authorize.Count > 0;

    /// <summary>The surface stated a posture, either gated or intentionally public.</summary>
    public bool Declares => HasAuthorize || AllowAnonymous;

    /// <summary>Both markers are present, which contradicts itself.</summary>
    public bool Conflicts => HasAuthorize && AllowAnonymous;

    /// <summary>An attribute from another framework is standing in for a Trax declaration.</summary>
    public bool HasForeign => ForeignAttributes.Count > 0;
}
