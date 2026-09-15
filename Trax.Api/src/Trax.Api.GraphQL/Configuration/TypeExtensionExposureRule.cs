namespace Trax.Api.GraphQL.Configuration;

/// <summary>
/// What a type-extension field inherits from the type it was grafted onto.
/// </summary>
internal enum TypeExtensionParentPosture
{
    /// <summary>
    /// The parent carries <c>[TraxAuthorize]</c>, so its <c>@authorize</c> directive already
    /// gates every field on it. The field needs no posture of its own.
    /// </summary>
    Gated,

    /// <summary>
    /// The parent is <c>[TraxAllowAnonymous]</c>, or is a schema root type. Either way the
    /// field inherits no gate, so it has to declare one.
    /// </summary>
    Anonymous,

    /// <summary>
    /// The parent is neither a gated nor an anonymous exposed surface: it reaches the schema
    /// only inside some other surface's output, and that surface's posture governs.
    /// </summary>
    NotExposed,
}

/// <summary>
/// The exposure rule for a field added to a type by a HotChocolate type extension, most often
/// an <c>[ExtendObjectType]</c> class.
/// </summary>
/// <remarks>
/// <para>
/// A type extension is a field on a type Trax owns, contributed by a class Trax hands to
/// HotChocolate and never revisits, so it is invisible to the census that
/// <see cref="ExposureAuthorizationRule"/> runs over trains and <c>[TraxQueryModel]</c>
/// entities. What gates such a field in practice is inheritance from the parent type's
/// <c>@authorize</c>, which holds right up until the parent has no gate to inherit.
/// </para>
/// <para>
/// The markers are Trax's own <c>[TraxAuthorize]</c> and <c>[TraxAllowAnonymous]</c>, which apply
/// to a resolver method and which Trax turns into the server's <c>@authorize</c> directive.
/// HotChocolate's attributes are refused in their place: see
/// <see cref="Trax.Effect.Attributes.TraxAuthorization"/>.
/// </para>
/// </remarks>
internal static class TypeExtensionExposureRule
{
    /// <summary>
    /// Evaluates one type-extension field. <paramref name="endpointGated"/> is whether the
    /// GraphQL endpoint opted into <c>RequireAuthorization()</c>.
    /// </summary>
    /// <remarks>
    /// This diverges from <see cref="ExposureAuthorizationRule"/> in one place, deliberately.
    /// An entity's <c>[TraxAllowAnonymous]</c> under a gated endpoint is a promise the endpoint
    /// cannot keep, and is reported. A field's <c>[AllowAnonymous]</c> under a gated endpoint is
    /// not the same statement: it punches a hole in the parent type's <c>@authorize</c>, so on a
    /// role-gated parent it still means something ("any authenticated caller, not just the
    /// role"). Reporting it would reject a posture that works.
    /// </remarks>
    public static ExposureViolation Evaluate(
        TypeExtensionParentPosture parent,
        bool hasAuthorize,
        bool hasAllowAnonymous,
        bool endpointGated
    )
    {
        // Declaring both contradicts itself wherever the field sits, including on a parent whose
        // gate it would otherwise inherit, so this is checked before the parent is consulted.
        if (hasAuthorize && hasAllowAnonymous)
            return ExposureViolation.Conflict;

        // The endpoint gate covers every field behind it, so nothing has to declare.
        if (endpointGated)
            return ExposureViolation.None;

        // A field that inherits a gate, or that is only reachable inside a gated surface's
        // output, is already behind a boundary somebody chose.
        if (parent is not TypeExtensionParentPosture.Anonymous)
            return ExposureViolation.None;

        // The parent is open and the field inherits nothing: the same question the census asks
        // of a train or an entity, answered by the same function.
        return ExposureAuthorizationRule.Evaluate(hasAuthorize, hasAllowAnonymous, endpointGated);
    }

    /// <summary>
    /// Builds the host-startup failure message. <paramref name="fieldPath"/> is the schema
    /// coordinate (<c>Issue.content</c>) and <paramref name="resolver"/> the CLR member behind
    /// it, because the coordinate alone does not say which file to open.
    /// </summary>
    public static string BuildMessage(
        string fieldPath,
        string resolver,
        string parentDescription,
        ExposureViolation violation
    ) =>
        violation switch
        {
            ExposureViolation.MissingMarker =>
                $"GraphQL field '{fieldPath}' is added by a type extension ({resolver}) onto "
                    + $"{parentDescription}, so it inherits no authorization gate, and it declares "
                    + "none of its own. A field that inherits nothing must state its posture "
                    + "explicitly: add [TraxAuthorize] (optionally with Policy or Roles) to gate "
                    + "it, or [TraxAllowAnonymous] to open it to anonymous callers. Both apply to "
                    + "a method, and Trax emits the matching @authorize directive. To gate the "
                    + "entire endpoint instead, call "
                    + "UseTraxGraphQL(configure: e => e.RequireAuthorization(...)).",
            ExposureViolation.Conflict =>
                $"GraphQL field '{fieldPath}' ({resolver}) declares both [TraxAuthorize] and "
                    + "[TraxAllowAnonymous]. The two are mutually exclusive: [TraxAllowAnonymous] "
                    + "opens the field to anonymous callers, while [TraxAuthorize] gates it. "
                    + "Pick one.",
            _ => throw new ArgumentOutOfRangeException(nameof(violation), violation, null),
        };

    /// <summary>
    /// The message for a posture declared on the type-extension class rather than on its
    /// resolvers, where the target is a schema root type.
    /// </summary>
    /// <remarks>
    /// Kept for a posture Trax cannot place. <c>[TraxAuthorize]</c> on an <c>[ExtendObjectType]</c>
    /// class applies to the fields that extension contributes, which is what someone writing it
    /// there means, so it needs no diagnostic of its own.
    /// </remarks>
    public static string BuildClassLevelMessage(
        string fieldPath,
        string extensionClass,
        string parentDescription
    ) =>
        $"GraphQL field '{fieldPath}' is added by a type extension ({extensionClass}) whose "
        + $"class-level posture cannot be applied to {parentDescription}. Move the attribute onto "
        + "the resolver method.";
}
