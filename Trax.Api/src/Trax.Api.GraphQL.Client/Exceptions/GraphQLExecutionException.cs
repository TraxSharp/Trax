using GraphQL;

namespace Trax.Api.GraphQL.Client;

public class GraphQLExecutionException : Exception
{
    public IReadOnlyList<GraphQLError> Errors { get; }

    public GraphQLExecutionException(IReadOnlyList<GraphQLError> errors)
        : base(BuildMessage(errors))
    {
        Errors = errors;
    }

    public GraphQLExecutionException(string message, Exception inner)
        : base(message, inner)
    {
        Errors = Array.Empty<GraphQLError>();
    }

    private static string BuildMessage(IReadOnlyList<GraphQLError> errors)
    {
        if (errors.Count == 0)
            return "GraphQL request returned no errors but execution failed.";
        return "GraphQL request returned errors: "
            + string.Join("; ", errors.Select(e => e.Message));
    }
}
