namespace Trax.Api.GraphQL.Client;

public class GraphQLSchemaIntrospectionException : Exception
{
    public GraphQLSchemaIntrospectionException(string message)
        : base(message) { }

    public GraphQLSchemaIntrospectionException(string message, Exception inner)
        : base(message, inner) { }
}
