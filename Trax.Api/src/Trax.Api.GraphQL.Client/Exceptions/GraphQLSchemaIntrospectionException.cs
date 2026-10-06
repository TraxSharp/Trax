namespace Trax.Api.GraphQL.Client;

/// <summary>
/// Thrown when the schema that requests are validated against cannot be loaded: introspection
/// failed or returned errors, an SDL file is missing, empty or unreadable, or the SDL does not
/// build into a schema. A schema provider caches its first load, so after this is thrown the
/// provider keeps failing until the process restarts.
/// </summary>
public class GraphQLSchemaIntrospectionException : Exception
{
    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">What went wrong.</param>
    public GraphQLSchemaIntrospectionException(string message)
        : base(message) { }

    /// <summary>Creates the exception with a message and its cause.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="inner">The underlying cause.</param>
    public GraphQLSchemaIntrospectionException(string message, Exception inner)
        : base(message, inner) { }
}
