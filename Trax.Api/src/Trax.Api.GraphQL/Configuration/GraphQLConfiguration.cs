namespace Trax.Api.GraphQL.Configuration;

/// <summary>
/// Holds the resolved configuration for the Trax GraphQL schema,
/// including discovered query model registrations.
/// </summary>
public class GraphQLConfiguration
{
    public IReadOnlyList<QueryModelRegistration> ModelRegistrations { get; }

    /// <summary>
    /// Tracks which namespace base types and namespace fields have been registered
    /// across type modules to prevent duplicate registrations. Populated at runtime
    /// by <c>TrainTypeModule</c> and <c>QueryModelTypeModule</c>.
    /// </summary>
    internal HashSet<string> RegisteredNamespaceTypes { get; } = new(StringComparer.Ordinal);

    public GraphQLConfiguration(IReadOnlyList<QueryModelRegistration> modelRegistrations)
    {
        ModelRegistrations = modelRegistrations;
    }
}
