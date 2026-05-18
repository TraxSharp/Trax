using GraphQL;

namespace Trax.Api.GraphQL.Client;

public class GraphQLValidationException : Exception
{
    public string Query { get; }
    public IReadOnlyList<ExecutionError> Errors { get; }

    public GraphQLValidationException(string query, IReadOnlyList<ExecutionError> errors)
        : base(BuildMessage(query, errors, null))
    {
        Query = query;
        Errors = errors;
    }

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
