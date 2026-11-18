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
