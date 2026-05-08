namespace Trax.Samples.PersistedOperations;

/// <summary>
/// GraphQL namespace constants. Trains sharing a namespace are grouped
/// under the same sub-field in the schema (e.g.
/// <c>discover { greeting { greet(...) { ... } } }</c>).
/// </summary>
public static class GraphQLNamespaces
{
    public const string Greeting = "greeting";
    public const string Users = "users";
}
