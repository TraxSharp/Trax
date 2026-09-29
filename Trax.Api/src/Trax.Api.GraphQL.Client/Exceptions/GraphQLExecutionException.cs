using GraphQL;

namespace Trax.Api.GraphQL.Client;

/// <summary>
/// Thrown by <see cref="IGraphQLClientExecutor.Run{TReturn}"/> when the server answered with GraphQL
/// errors, when the response carried no data, or when the data could not be extracted into the
/// response type.
/// </summary>
public class GraphQLExecutionException : Exception
{
    /// <summary>The errors the server returned. Empty when the failure was a missing or unreadable response.</summary>
    public IReadOnlyList<GraphQLError> Errors { get; }

    /// <summary>Creates the exception for errors the server returned; the message joins their messages.</summary>
    /// <param name="errors">The server's errors.</param>
    public GraphQLExecutionException(IReadOnlyList<GraphQLError> errors)
        : base(BuildMessage(errors))
    {
        Errors = errors;
    }

    /// <summary>Creates the exception for a response that could not be used; <see cref="Errors"/> is empty.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="inner">The underlying cause.</param>
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
