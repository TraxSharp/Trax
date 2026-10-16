using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Features;
using HotChocolate.Language;
using HotChocolate.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace Trax.Api.GraphQL.PersistedOperations.Services;

/// <summary>
/// Runs every generated document through the validation the host runs on a request (its depth limit, the operation cap
/// and Trax's own rules among them), so a document the host would refuse on every call refuses the host's start
/// instead, naming what generated it.
/// </summary>
/// <remarks>
/// Authorization is left to run time: there is no caller at startup, and every Trax gate applies before the resolver,
/// so an error HotChocolate's authorization reports here (its codes start <c>AUTH_</c>) is not counted. Cost analysis
/// runs in the request pipeline, not in validation, so a document over the cost limit is still refused per call.
/// </remarks>
internal static class GeneratedDocumentValidation
{
    /// <exception cref="InvalidOperationException">A document fails the host's validation.</exception>
    public static void Validate(ISchemaDefinition schema, IEnumerable<GeneratedDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(documents);

        var refusals = new List<string>();
        foreach (var document in documents)
        {
            var errors = Errors(schema, document.Id, document.Text);
            if (errors.Count > 0)
                refusals.Add(
                    $"The document of the {document.Owner} is refused by this host's own validation, so "
                        + $"every call to it would be: {string.Join(" ", errors)}"
                );
        }
        if (refusals.Count > 0)
            throw new InvalidOperationException(
                string.Join("\n", refusals)
                    + "\nRaise the limit (MaxExecutionDepth, MaxOperationsPerRequest), or give the answer a shallower "
                    + "shape."
            );
    }

    /// <summary>
    /// What the host's validation says of <paramref name="text"/>, but authorization: nothing when it would run.
    /// </summary>
    public static IReadOnlyList<string> Errors(ISchemaDefinition schema, string id, string text)
    {
        var validator = schema.Services.GetRequiredService<DocumentValidator>();
        var result = validator.Validate(
            schema,
            new OperationDocumentId(id),
            Utf8GraphQLParser.Parse(text),
            new FeatureCollection(),
            onlyNonCacheable: false
        );
        return result
            .Errors.Where(e => e.Code?.StartsWith("AUTH_", StringComparison.Ordinal) != true)
            .Select(e => e.Message)
            .ToList();
    }
}
