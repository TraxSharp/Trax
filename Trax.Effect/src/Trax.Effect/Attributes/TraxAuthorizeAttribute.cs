namespace Trax.Effect.Attributes;

/// <summary>
/// Specifies authorization requirements for a train when executed via the Trax API.
/// </summary>
/// <remarks>
/// When a train is executed through the REST or GraphQL API, the framework checks
/// for this attribute and enforces the specified authorization requirements against
/// the current HTTP user before allowing execution.
///
/// Trains without this attribute have no per-train authorization requirements
/// (though endpoint-level auth from the <c>configure</c> callback still applies).
///
/// Multiple attributes can be combined — all must be satisfied.
/// The scheduler bypasses this check entirely since it is trusted infrastructure.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public class TraxAuthorizeAttribute : Attribute
{
    /// <summary>
    /// The name of an ASP.NET Core authorization policy that must be satisfied.
    /// </summary>
    public string? Policy { get; init; }

    /// <summary>
    /// A comma-separated list of roles. The user must have at least one of these roles.
    /// </summary>
    public string? Roles { get; init; }

    public TraxAuthorizeAttribute() { }

    /// <summary>
    /// Creates a new <see cref="TraxAuthorizeAttribute"/> requiring the specified policy.
    /// </summary>
    /// <param name="policy">The authorization policy name.</param>
    public TraxAuthorizeAttribute(string policy) => Policy = policy;
}
