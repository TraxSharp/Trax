using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Attributes;

namespace Trax.Mediator.Services.TrainDiscovery;

/// <summary>
/// One train found in the DI container by <see cref="ITrainDiscoveryService"/>: its types, lifetime,
/// authorization requirements, and the attributes that decide how Trax exposes and runs it.
/// Built by discovery; the Scheduler, Api and Dashboard read it.
/// </summary>
public class TrainRegistration
{
    /// <summary>
    /// The type the train is resolved by: its service interface (for example <c>IBanPlayerTrain</c>)
    /// when one is registered, otherwise the concrete class. Its <see cref="Type.FullName"/> is the
    /// train's canonical name, used for lookups, work queue entries and metadata.
    /// </summary>
    public required Type ServiceType { get; init; }

    /// <summary>
    /// The concrete train class. Attributes such as <c>[TraxAuthorize]</c> and
    /// <c>[TraxMutation]</c> are read from it and its interfaces.
    /// </summary>
    public required Type ImplementationType { get; init; }

    /// <summary>
    /// The train's <c>TIn</c>. Unique across registrations: each input type maps to one train.
    /// </summary>
    public required Type InputType { get; init; }

    /// <summary>The train's <c>TOut</c>; <c>Unit</c> for a train with no output.</summary>
    public required Type OutputType { get; init; }

    /// <summary>The DI lifetime the train was registered with.</summary>
    public required ServiceLifetime Lifetime { get; init; }

    /// <summary>
    /// The short name of <see cref="ServiceType"/>, without namespace, generics written as
    /// <c>Name&lt;Arg&gt;</c>. Accepted as a train name by <c>ITrainExecutionService</c> when no
    /// other train shares it.
    /// </summary>
    public required string ServiceTypeName { get; init; }

    /// <summary>The short name of <see cref="ImplementationType"/>, formatted like <see cref="ServiceTypeName"/>.</summary>
    public required string ImplementationTypeName { get; init; }

    /// <summary>The short name of <see cref="InputType"/>, formatted like <see cref="ServiceTypeName"/>.</summary>
    public required string InputTypeName { get; init; }

    /// <summary>The short name of <see cref="OutputType"/>, formatted like <see cref="ServiceTypeName"/>.</summary>
    public required string OutputTypeName { get; init; }

    /// <summary>
    /// The distinct, non-blank <c>Policy</c> values from every <see cref="TraxAuthorizeAttribute"/>
    /// on the implementation, its base classes and its interfaces. The caller must satisfy all of
    /// them. Empty when there are none, which on its own does not mean the train is unprotected:
    /// see <see cref="HasAuthorizeAttribute"/>.
    /// </summary>
    public required IReadOnlyList<string> RequiredPolicies { get; init; }

    /// <summary>
    /// The roles from every <see cref="TraxAuthorizeAttribute"/> on the train, exactly as declared:
    /// split on <c>','</c>, trimmed, de-duplicated ordinally, and never case-folded. The caller must
    /// hold one of them, compared ordinally against its role claims, as <c>@authorize</c> does.
    /// </summary>
    public required IReadOnlyList<string> RequiredRoles { get; init; }

    /// <summary>
    /// True when the implementation, a base class or an implemented interface carries at least one
    /// <see cref="TraxAuthorizeAttribute"/>, including the parameterless form. A bare <c>[TraxAuthorize]</c> sets this flag with empty
    /// policies and roles; authorization enforcement treats it as "require an authenticated user."
    /// </summary>
    public bool HasAuthorizeAttribute { get; init; }

    /// <summary>
    /// True when the implementation carries <see cref="TraxAllowAnonymousAttribute"/> directly,
    /// via a base class, or via any implemented interface. On a GraphQL-exposed train this is the
    /// explicit "intentionally public" marker that satisfies the exposure check; it carries no
    /// runtime gate of its own (train authorization is enforced via <see cref="HasAuthorizeAttribute"/>).
    /// Mutually exclusive with <see cref="HasAuthorizeAttribute"/> on an exposed train.
    /// </summary>
    public bool HasAllowAnonymousAttribute { get; init; }

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

    /// <summary>
    /// Whether the implementation overrides <c>ServiceTrain.QueueSubjectKey</c>, directly or through
    /// a base class. Only such a train can stamp a subject key on its queue entry, so only its
    /// queued work can be serialized against other work for the same subject. Computed at discovery
    /// with the same check the enqueue uses to decide whether to ask the train for a key.
    /// </summary>
    /// <remarks>
    /// True does not mean every entry has a key: the override may return null for a given input.
    /// </remarks>
    public bool HasQueueSubjectKey { get; init; }
}
