using HotChocolate;
using HotChocolate.Language;
using HotChocolate.Validation;

namespace Trax.Api.GraphQL.Validation;

/// <summary>
/// Validates that a single GraphQL request does not submit more than the
/// configured number of top-level selections. Aliased fields and batched
/// operations both count: a request with <c>a: foo b: foo c: foo</c> has three,
/// and a payload with two operations each carrying four root fields has eight.
/// </summary>
/// <remarks>
/// Guards against amplification / request-fanout denial of service where a
/// single authenticated caller issues hundreds of aliased mutation invocations
/// in one HTTP round-trip. Each invocation still goes through
/// authorization, but even authorized fan-out can exhaust connection pools,
/// queue capacity, or downstream services.
/// </remarks>
internal sealed class OperationCountValidatorRule(int maxOperations) : IDocumentValidatorRule
{
    public bool IsCacheable => true;

    public ushort Priority => 0;

    public void Validate(DocumentValidatorContext context, DocumentNode document)
    {
        var total = 0;
        foreach (var definition in document.Definitions)
        {
            if (definition is not OperationDefinitionNode op)
                continue;
            total += op.SelectionSet.Selections.Count;
            if (total <= maxOperations)
                continue;

            context.ReportError(
                ErrorBuilder
                    .New()
                    .SetMessage(
                        $"The request exceeds the maximum allowed selections per request ({maxOperations})."
                    )
                    .SetCode("TRAX_TOO_MANY_OPERATIONS")
                    .Build()
            );
            return;
        }
    }
}
