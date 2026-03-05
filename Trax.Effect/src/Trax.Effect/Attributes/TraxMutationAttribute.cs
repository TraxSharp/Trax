namespace Trax.Effect.Attributes;

/// <summary>
/// Exposes a train as typed GraphQL mutation field(s) under <c>dispatch</c>.
/// Generates <c>run{Name}</c> and/or <c>queue{Name}</c> mutations based on
/// the <see cref="Operations"/> property.
/// </summary>
/// <remarks>
/// Place this attribute on the concrete train class. The generated field names are derived
/// by stripping the <c>I</c> prefix and <c>Train</c> suffix from the service type name,
/// then prepending <c>run</c> or <c>queue</c>
/// (e.g. <c>IBanPlayerTrain</c> → <c>runBanPlayer</c> / <c>queueBanPlayer</c>).
///
/// Trains without this attribute (or <see cref="TraxQueryAttribute"/>) are not exposed
/// as GraphQL endpoints. A train cannot have both attributes.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public class TraxMutationAttribute : Attribute
{
    /// <summary>
    /// Overrides the auto-derived GraphQL field name.
    /// When null, the name is derived by stripping the "I" prefix and "Train" suffix
    /// from the service type name (e.g. <c>IBanPlayerTrain</c> → <c>BanPlayer</c>).
    /// This produces <c>runBanPlayer</c> and/or <c>queueBanPlayer</c>.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// A human-readable description that appears in the GraphQL schema documentation.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Marks the generated mutations as deprecated in the GraphQL schema.
    /// Clients see a deprecation warning during introspection.
    /// </summary>
    public string? DeprecationReason { get; init; }

    /// <summary>
    /// Controls which mutation operations are generated.
    /// Defaults to <see cref="GraphQLOperation.Run"/> (synchronous execution only).
    /// Set to <see cref="GraphQLOperation.Queue"/> for scheduler dispatch only,
    /// or <see cref="GraphQLOperation.RunAndQueue"/> for both.
    /// </summary>
    public GraphQLOperation Operations { get; init; } = GraphQLOperation.Run;
}

/// <summary>
/// Controls which typed mutation operations are generated for a train.
/// </summary>
[Flags]
public enum GraphQLOperation
{
    /// <summary>Generate only the <c>run{Name}</c> mutation (synchronous execution).</summary>
    Run = 1,

    /// <summary>Generate only the <c>queue{Name}</c> mutation (scheduler dispatch).</summary>
    Queue = 2,

    /// <summary>Generate both <c>run{Name}</c> and <c>queue{Name}</c> mutations.</summary>
    RunAndQueue = Run | Queue,
}
