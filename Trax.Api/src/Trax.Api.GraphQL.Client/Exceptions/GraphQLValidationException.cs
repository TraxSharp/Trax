using GraphQL;

namespace Trax.Api.GraphQL.Client;

/// <summary>
/// Thrown when a request's query fails validation against the server's schema, or has no
/// operation definition. Raised before anything is sent.
/// </summary>
public class GraphQLValidationException : Exception
{
    /// <summary>The query text that failed.</summary>
    public string Query { get; }

    /// <summary>The validation errors. Empty when the query had no operation definition.</summary>
    public IReadOnlyList<ExecutionError> Errors { get; }

    /// <summary>Creates the exception for a query that failed schema validation.</summary>
    /// <param name="query">The query text.</param>
    /// <param name="errors">The validation errors.</param>
    public GraphQLValidationException(string query, IReadOnlyList<ExecutionError> errors)
        : base(BuildMessage(query, errors, null))
    {
        Query = query;
        Errors = errors;
    }

    /// <summary>Creates the exception with a custom message head in place of the default one.</summary>
    /// <param name="query">The query text.</param>
    /// <param name="errors">The validation errors, possibly empty.</param>
    /// <param name="detail">The message head.</param>
    public GraphQLValidationException(
        string query,
        IReadOnlyList<ExecutionError> errors,
        string detail
    )
        : base(BuildMessage(query, errors, detail))
    {
        Query = query;
        Errors = errors;
    }

    private static string BuildMessage(
        string query,
        IReadOnlyList<ExecutionError> errors,
        string? detail
    )
    {
        var head = detail ?? "GraphQL query failed schema validation";
        var joined =
            errors.Count == 0 ? "" : ": " + string.Join("; ", errors.Select(e => e.Message));
        return $"{head}{joined}. Query: {query}";
    }
}
