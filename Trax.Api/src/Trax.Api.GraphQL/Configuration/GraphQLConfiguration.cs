using HotChocolate.Execution.Configuration;

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

    public GraphQLConfiguration(
        IReadOnlyList<QueryModelRegistration> modelRegistrations,
        IReadOnlyList<Type> additionalTypeModules,
        IReadOnlyList<Action<IRequestExecutorBuilder>> schemaConfigurations
    )
    {
        ModelRegistrations = modelRegistrations;
        AdditionalTypeModules = additionalTypeModules;
        SchemaConfigurations = schemaConfigurations;
    }
}
