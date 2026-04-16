using HotChocolate.CostAnalysis;
using HotChocolate.Execution.Configuration;
using Microsoft.AspNetCore.Http;

namespace Trax.Api.GraphQL.Configuration;

/// <summary>
/// Holds the resolved configuration for the Trax GraphQL schema,
/// including discovered query model registrations.
/// </summary>
public class GraphQLConfiguration
{
    public IReadOnlyList<QueryModelRegistration> ModelRegistrations { get; }

    /// <summary>
    /// Additional HotChocolate <see cref="HotChocolate.Types.TypeModule"/> types
    /// registered by consumers via <c>AddTypeModule&lt;T&gt;()</c>.
    /// </summary>
    internal IReadOnlyList<Type> AdditionalTypeModules { get; }

    /// <summary>
    /// Additional HotChocolate type extension classes (e.g. <c>[ExtendObjectType]</c>)
    /// registered by consumers via <c>AddTypeExtension&lt;T&gt;()</c> or
    /// <c>AddTypeExtensions(assembly)</c>.
    /// </summary>
    internal IReadOnlyList<Type> AdditionalTypeExtensions { get; }

    /// <summary>
    /// Callbacks to apply arbitrary <see cref="IRequestExecutorBuilder"/> configuration
    /// registered by consumers via <c>ConfigureSchema()</c>.
    /// </summary>
    internal IReadOnlyList<Action<IRequestExecutorBuilder>> SchemaConfigurations { get; }

    /// <summary>
    /// Tracks which namespace base types and namespace fields have been registered
    /// across type modules to prevent duplicate registrations. Populated at runtime
    /// by <c>TrainTypeModule</c> and <c>QueryModelTypeModule</c>.
    /// </summary>
    internal HashSet<string> RegisteredNamespaceTypes { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Max GraphQL execution depth (default 4). Queries deeper than this are rejected
    /// during validation.
    /// </summary>
    public int MaxExecutionDepth { get; }

    /// <summary>
    /// Optional cost-analysis override. Applied after Trax defaults.
    /// </summary>
    internal Action<CostOptions>? CostOverride { get; }

    /// <summary>
    /// Predicate that gates introspection per request. Null → default
    /// (Development-only). Returns <c>true</c> to allow, <c>false</c> to deny.
    /// </summary>
    internal Predicate<HttpContext>? IntrospectionPredicate { get; }

    /// <summary>
    /// Maximum top-level GraphQL selections per request (default 50).
    /// </summary>
    public int MaxOperationsPerRequest { get; }

    /// <summary>
    /// True when <c>RequireAuthorization()</c> was called on the builder.
    /// Gates GraphQL execution (HTTP POST and GET-with-query); the BCP tool
    /// page and schema introspection are governed independently.
    /// </summary>
    internal bool AuthorizationRequired { get; }

    /// <summary>
    /// Authorization policy applied by the execution interceptor when
    /// <see cref="AuthorizationRequired"/> is true. <c>null</c> means
    /// "use the combined Trax auth policy" — every <c>AddTrax*Auth</c>
    /// registers its scheme into that policy.
    /// </summary>
    internal string? AuthorizationPolicy { get; }

    public GraphQLConfiguration(
        IReadOnlyList<QueryModelRegistration> modelRegistrations,
        IReadOnlyList<Type> additionalTypeModules,
        IReadOnlyList<Action<IRequestExecutorBuilder>> schemaConfigurations,
        IReadOnlyList<Type> additionalTypeExtensions,
        int maxExecutionDepth = 4,
        Action<CostOptions>? costOverride = null,
        Predicate<HttpContext>? introspectionPredicate = null,
        int maxOperationsPerRequest = 50,
        bool authorizationRequired = false,
        string? authorizationPolicy = null
    )
    {
        ModelRegistrations = modelRegistrations;
        AdditionalTypeModules = additionalTypeModules;
        SchemaConfigurations = schemaConfigurations;
        AdditionalTypeExtensions = additionalTypeExtensions;
        MaxExecutionDepth = maxExecutionDepth;
        CostOverride = costOverride;
        IntrospectionPredicate = introspectionPredicate;
        MaxOperationsPerRequest = maxOperationsPerRequest;
        AuthorizationRequired = authorizationRequired;
        AuthorizationPolicy = authorizationPolicy;
    }
}
