using HotChocolate.Language;

namespace Trax.Api.GraphQL.PersistedOperations.Middleware;

/// <summary>
/// Parses a request document once, for the carve-out checks that need its structure.
/// </summary>
/// <remarks>
/// Both carve-outs — introspection and the management surface — have to answer a question about
/// what the document selects, and neither can answer it from the text. Parsing in one place means
/// a document is parsed once per request rather than once per carve-out, and removes the ordering
/// subtlety where a cheaper text check could decide before the parser was consulted.
/// </remarks>
internal static class GraphQLDocumentParser
{
    /// <summary>
    /// The parsed document, or null when it is empty or does not parse. Callers treat null as
    /// "cannot be shown to be inside a carve-out", which leaves the rejection path to run.
    /// </summary>
    public static DocumentNode? TryParse(string? document)
    {
        if (string.IsNullOrEmpty(document))
            return null;

        try
        {
            return Utf8GraphQLParser.Parse(document);
        }
        catch
        {
            return null;
        }
    }
}
