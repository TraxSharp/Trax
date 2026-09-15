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

/// <summary>
/// Reads the Trax authorization vocabulary off a type or a method, and reports any foreign
/// authorization attribute found alongside it.
/// </summary>
/// <remarks>
/// <para>
/// Trax owns its own vocabulary on purpose. <see cref="TraxAuthorizeAttribute"/> and
/// <see cref="TraxAllowAnonymousAttribute"/> are what a consumer writes, whatever GraphQL server
/// sits underneath, and Trax translates them into whatever that server needs. A consumer writing
/// HotChocolate's attributes instead couples their code to a dependency Trax exists to hide, and
/// splits the vocabulary in two: the same question would then be answered by two attributes with
/// different combinator rules and different inheritance behaviour.
/// </para>
/// <para>
/// The foreign attributes are matched by full name rather than by type, because this assembly
/// does not reference HotChocolate and must not start to. A GraphQL server is an
/// <c>Trax.Api</c>-layer concern.
/// </para>
/// </remarks>
public static class TraxAuthorization
{
    /// <summary>
    /// Authorization attributes Trax refuses to read, by full name. HotChocolate's own pair is
    /// the live case: both apply to a resolver method, so before Trax widened its attributes they
    /// were the only thing that compiled there.
    /// </summary>
    /// <remarks>
    /// ASP.NET Core's <c>[Authorize]</c> is deliberately absent. It governs endpoints and MVC
    /// actions, which is a different surface that Trax does not own, and banning it there would
    /// be wrong.
    /// </remarks>
    public static IReadOnlyList<string> ForeignAuthorizationAttributes { get; } =
    [
        "HotChocolate.Authorization.AuthorizeAttribute",
        "HotChocolate.Authorization.AllowAnonymousAttribute",
        // HotChocolate 15 and earlier; a consumer mid-upgrade can still have these.
        "HotChocolate.AspNetCore.Authorization.AuthorizeAttribute",
        "HotChocolate.AspNetCore.Authorization.AllowAnonymousAttribute",
    ];

    /// <summary>
    /// Reads the declaration on a method. Inherited attributes count, so an override of a
    /// declared base method keeps the base's posture.
    /// </summary>
    public static TraxAuthorizationDeclaration Read(MethodInfo method)
    {
        ArgumentNullException.ThrowIfNull(method);
        return Read((MemberInfo)method);
    }

    /// <summary>
    /// Reads the declaration on a type, including attributes inherited from base classes and
    /// declared on implemented interfaces.
    /// </summary>
    public static TraxAuthorizationDeclaration Read(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var seen = new HashSet<TraxAuthorizeAttribute>(ReferenceEqualityComparer.Instance);
        var authorize = new List<TraxAuthorizeAttribute>();

        // The type itself, plus every interface it implements. Inherited = true on the attribute
        // already walks the base-class chain.
        foreach (var carrier in new[] { type }.Concat(type.GetInterfaces()))
        {
            foreach (
                var attribute in carrier.GetCustomAttributes<TraxAuthorizeAttribute>(inherit: true)
            )
            {
                if (seen.Add(attribute))
                    authorize.Add(attribute);
            }
        }

        var anonymous =
            type.IsDefined(typeof(TraxAllowAnonymousAttribute), inherit: true)
            || type.GetInterfaces()
                .Any(i => i.IsDefined(typeof(TraxAllowAnonymousAttribute), inherit: true));

        return new TraxAuthorizationDeclaration(authorize, anonymous, Foreign(type));
    }

    private static TraxAuthorizationDeclaration Read(MemberInfo member)
    {
        var seen = new HashSet<TraxAuthorizeAttribute>(ReferenceEqualityComparer.Instance);
        var authorize = new List<TraxAuthorizeAttribute>();

        foreach (var attribute in member.GetCustomAttributes<TraxAuthorizeAttribute>(inherit: true))
        {
            if (seen.Add(attribute))
                authorize.Add(attribute);
        }

        return new TraxAuthorizationDeclaration(
            authorize,
            member.IsDefined(typeof(TraxAllowAnonymousAttribute), inherit: true),
            Foreign(member)
        );
    }

    private static IReadOnlyList<string> Foreign(MemberInfo member) =>
        member
            .GetCustomAttributes(inherit: true)
            .Select(a => a.GetType().FullName)
            .Where(name => name is not null && ForeignAuthorizationAttributes.Contains(name))
            .Select(name => name!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// The failure message for a surface that declared its posture with another framework's
    /// attribute. <paramref name="subject"/> describes what was decorated, for example
    /// <c>"GraphQL field 'Issue.content' (Nwyc.IssueContentExtension.GetContent)"</c>.
    /// </summary>
    public static string ForeignAttributeMessage(
        string subject,
        IReadOnlyList<string> foreignAttributes
    ) =>
        $"{subject} declares its authorization posture with "
        + string.Join(", ", foreignAttributes.Select(Short))
        + ", which Trax does not read. Trax owns this vocabulary so a surface declares the same "
        + "way wherever it lives and whatever GraphQL server is underneath: use [TraxAuthorize] "
        + "to gate it, or [TraxAllowAnonymous] to open it to anonymous callers. Both apply to a "
        + "method, so a resolver declares its own posture directly. Trax emits the matching "
        + "server directive.";

    /// <summary>
    /// Throws when the member declares a posture Trax cannot honour. Use where a single surface
    /// is being read; a caller collecting every offender at once reads
    /// <see cref="TraxAuthorizationDeclaration.ForeignAttributes"/> instead and reports them
    /// together.
    /// </summary>
    public static void Validate(TraxAuthorizationDeclaration declaration, string subject)
    {
        ArgumentNullException.ThrowIfNull(declaration);

        if (declaration.HasForeign)
            throw new InvalidOperationException(
                ForeignAttributeMessage(subject, declaration.ForeignAttributes)
            );
    }

    private static string Short(string fullName) =>
        "[" + fullName.Split('.')[^1].Replace("Attribute", string.Empty) + "]";
}
