using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Attributes;

namespace Trax.Mediator.Services.TrainDiscovery;

/// <summary>
/// Represents a discovered IServiceTrain registration in the DI container.
/// </summary>
public class TrainRegistration
{
    public required Type ServiceType { get; init; }
    public required Type ImplementationType { get; init; }
    public required Type InputType { get; init; }
    public required Type OutputType { get; init; }
    public required ServiceLifetime Lifetime { get; init; }

    public required string ServiceTypeName { get; init; }
    public required string ImplementationTypeName { get; init; }
    public required string InputTypeName { get; init; }
    public required string OutputTypeName { get; init; }

    public required IReadOnlyList<string> RequiredPolicies { get; init; }
    public required IReadOnlyList<string> RequiredRoles { get; init; }

    /// <summary>
    /// Whether this train is exposed as a typed GraphQL query field under <c>discover</c>.
    /// True when the implementation class has a <see cref="TraxQueryAttribute"/>.
    /// </summary>
    public required bool IsQuery { get; init; }

    /// <summary>
    /// Whether this train is exposed as typed GraphQL mutation field(s) under <c>dispatch</c>.
    /// True when the implementation class has a <see cref="TraxMutationAttribute"/>.
    /// </summary>
    public required bool IsMutation { get; init; }

    /// <summary>
    /// Whether this train's lifecycle events are broadcast to GraphQL subscribers.
    /// True when the implementation class has a <see cref="TraxBroadcastAttribute"/>.
    /// </summary>
    public required bool IsBroadcastEnabled { get; init; }

    /// <summary>
    /// Whether this train should be dispatched to a remote worker when one is configured.
    /// True when the implementation class has a <see cref="TraxRemoteAttribute"/>.
    /// If no remote submitter is configured, this is silently ignored and the train runs locally.
    /// Builder-level routing via <c>ForTrain&lt;T&gt;()</c> takes precedence over this attribute.
    /// </summary>
    public required bool IsRemote { get; init; }

    /// <summary>
    /// GraphQL field name override from <see cref="TraxQueryAttribute.Name"/> or
    /// <see cref="TraxMutationAttribute.Name"/>.
    /// Null means the TypeModule derives the name automatically.
    /// </summary>
    public string? GraphQLName { get; init; }

    /// <summary>
    /// Description for the generated GraphQL fields.
    /// </summary>
    public string? GraphQLDescription { get; init; }

    /// <summary>
    /// If non-null, the generated fields are marked as deprecated.
    /// </summary>
    public string? GraphQLDeprecationReason { get; init; }

    /// <summary>
    /// Which operations (Run, Queue, or both) to generate. Only applies when <see cref="IsMutation"/> is true.
    /// </summary>
    public required GraphQLOperation GraphQLOperations { get; init; }

    /// <summary>
    /// Optional namespace to group this train's GraphQL field under.
    /// When set, the field appears under a sub-namespace (e.g. <c>discover { alerts { field } }</c>).
    /// </summary>
    public string? GraphQLNamespace { get; init; }

    /// <summary>
    /// Maximum concurrent RUN executions for this train, from <see cref="TraxConcurrencyLimitAttribute"/>.
    /// Null means no per-train limit (falls back to the global default or no limit).
    /// Builder-level overrides via <c>ConcurrentRunLimit&lt;T&gt;()</c> take precedence over this value.
    /// </summary>
    public int? MaxConcurrentRun { get; init; }
}
