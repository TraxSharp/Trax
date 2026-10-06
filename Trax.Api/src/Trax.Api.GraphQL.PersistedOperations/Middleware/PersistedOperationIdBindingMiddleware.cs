using System.Collections.Immutable;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Language;
using Microsoft.Extensions.DependencyInjection;

namespace Trax.Api.GraphQL.PersistedOperations.Middleware;

/// <summary>
/// Makes a persisted-operation id run only the document the store holds for it. A request that
/// carries both an id and a document is refused unless the id is that document's own hash, before
/// the document cache is consulted.
/// </summary>
/// <remarks>
/// <para>
/// HotChocolate keys its parsed-document cache on the request's document id. Trax ids are
/// operator-managed names rather than content hashes, so only the store binds a document to an
/// id, and a document that arrives with an id has no claim to
/// it. This is the same rule automatic persisted queries apply when they verify that the hash a
/// client sends matches the document it sends: the only id a document may arrive with is its own
/// hash, under the hash algorithm the executor is configured with (MD5 hex by default;
/// <c>AddSha256DocumentHashProvider</c> for Apollo-style SHA-256 ids).
/// </para>
/// <para>
/// A request with only an id, or only a document, is untouched. The refusal is the
/// <c>PERSISTED_OPERATION_ID_MISMATCH</c> error with HTTP status 400. See
/// <c>docs/adr/0026-a-persisted-operation-id-means-one-document-on-every-node.md</c>.
/// </para>
/// </remarks>
internal static class PersistedOperationIdBindingMiddleware
{
    public const string Key = "Trax.PersistedOperationIdBinding";

    internal const string ErrorCode = "PERSISTED_OPERATION_ID_MISMATCH";

    internal const string ErrorMessage =
        "A request that names a persisted operation id may carry a document only when the id is "
        + "that document's hash. Send the id alone to run the stored operation, or the document "
        + "alone to run it inline.";

    public static RequestDelegate Create(
        RequestMiddlewareFactoryContext factory,
        RequestDelegate next
    )
    {
        var hashProvider = factory.SchemaServices.GetRequiredService<IDocumentHashProvider>();

        return context =>
        {
            var request = context.Request;
            if (request.DocumentId.IsEmpty || request.Document is null)
                return next(context);

            if (IsOwnHash(request, hashProvider))
                return next(context);

            context.Result = Refusal();
            return default;
        };
    }

    private static bool IsOwnHash(IOperationRequest request, IDocumentHashProvider hashProvider)
    {
        // The transports compute DocumentHash from the bytes they received, with this executor's
        // hash provider. A request built in process may not carry one, so compute it from the
        // document as given.
        var hash = request.DocumentHash.IsEmpty
            ? hashProvider.ComputeHash(request.Document!.AsSpan())
            : request.DocumentHash;

        return string.Equals(hash.Value, request.DocumentId.Value, StringComparison.Ordinal);
    }

    private static OperationResult Refusal() =>
        new(
            ImmutableList.Create(
                ErrorBuilder.New().SetMessage(ErrorMessage).SetCode(ErrorCode).Build()
            ),
            null
        )
        {
            ContextData = ImmutableDictionary<string, object?>.Empty.Add(
                ExecutionContextData.HttpStatusCode,
                400
            ),
        };
}
