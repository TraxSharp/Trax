namespace Trax.Effect.Attributes;

/// <summary>
/// Exposes a train as a typed GraphQL mutation field under <c>dispatch</c>.
/// Pass one or more <see cref="GraphQLOperation"/> values to control which execution
/// modes are available. When no operations are specified, both <c>Run</c> and <c>Queue</c>
/// are enabled (the mutation gets an optional <c>mode: ExecutionMode</c> parameter).
/// </summary>
/// <remarks>
/// Place this attribute on the concrete train class. The generated field name is derived
/// by stripping the <c>I</c> prefix and <c>Train</c> suffix from the service type name,
/// then lowercasing the first character
/// (e.g. <c>IBanPlayerTrain</c> → <c>banPlayer</c>).
///
/// Trains without this attribute (or <see cref="TraxQueryAttribute"/>) are not exposed
/// as GraphQL endpoints. A train cannot have both attributes.
///
/// <example>
/// <code>
/// // Both run and queue (default — generates mode parameter)
/// [TraxMutation]
///
/// // Explicit both
/// [TraxMutation(GraphQLOperation.Run, GraphQLOperation.Queue)]
///
/// // Run only (no mode parameter)
/// [TraxMutation(GraphQLOperation.Run)]
///
/// // Queue only (no mode parameter, includes priority)
/// [TraxMutation(GraphQLOperation.Queue)]
/// </code>
/// </example>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public class TraxMutationAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance with the specified execution modes.
    /// When no operations are provided, both <see cref="GraphQLOperation.Run"/> and
    /// <see cref="GraphQLOperation.Queue"/> are enabled.
    /// </summary>
    /// <param name="operations">
    /// One or more execution modes. Pass <see cref="GraphQLOperation.Run"/> for synchronous-only,
    /// <see cref="GraphQLOperation.Queue"/> for queue-only, or both for a mutation with
    /// an optional <c>mode</c> parameter.
    /// </param>
    public TraxMutationAttribute(params GraphQLOperation[] operations)
    {
        Operations =
            operations.Length > 0
                ? operations.Aggregate((a, b) => a | b)
                : GraphQLOperation.Run | GraphQLOperation.Queue;
    }

    /// <summary>
    /// The combined execution modes for this mutation, computed from the constructor params.
    /// </summary>
    public GraphQLOperation Operations { get; }

    /// <summary>
    /// Overrides the auto-derived GraphQL field name.
    /// When null, the name is derived by stripping the "I" prefix and "Train" suffix
    /// from the service type name (e.g. <c>IBanPlayerTrain</c> → <c>banPlayer</c>).
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// A human-readable description that appears in the GraphQL schema documentation.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Marks the generated mutation as deprecated in the GraphQL schema.
    /// Clients see a deprecation warning during introspection.
    /// </summary>
    public string? DeprecationReason { get; init; }
}

/// <summary>
/// Controls which execution modes are available for a train's GraphQL mutation.
/// Pass one or more values to <see cref="TraxMutationAttribute"/> to configure behavior.
/// </summary>
[Flags]
public enum GraphQLOperation
{
    /// <summary>Synchronous execution — the mutation runs the train and returns the result immediately.</summary>
    Run = 1,

    /// <summary>Asynchronous execution — the mutation queues the train for later processing (includes priority parameter).</summary>
    Queue = 2,
}
