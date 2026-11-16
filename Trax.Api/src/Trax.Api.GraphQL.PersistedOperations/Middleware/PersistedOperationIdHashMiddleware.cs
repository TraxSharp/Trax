using HotChocolate.Execution;

namespace Trax.Api.GraphQL.PersistedOperations.Middleware;

/// <summary>
/// Puts the document's hash into the operation id of a request whose document id is not that hash,
/// so every HotChocolate cache keyed on the operation id is keyed on the document as well.
/// </summary>
/// <remarks>
/// HotChocolate derives the operation id from the document id, which for a persisted operation is
/// the operator's name for it, and keys caches on it that Trax cannot replace or empty: since 16.7
/// the normalized-document cache inside its operation compiler is one. A re-upload under the same
/// id would otherwise compile the old document after Trax's own caches were emptied. With the hash
/// in the key, a changed document is a different operation to every such cache. Runs after the
/// document is resolved and before it is validated: nothing normalizes an unvalidated document,
/// and the cost analyzer and authorization, which run before the operation cache, normalize it. See
/// <c>docs/adr/0034-a-cached-persisted-operation-is-never-older-than-the-last-change-or-its-maximum-age.md</c>.
/// </remarks>
internal static class PersistedOperationIdHashMiddleware
{
    public const string Key = "Trax.PersistedOperationIdHash";

    public static RequestDelegate Create(
        RequestMiddlewareFactoryContext factory,
        RequestDelegate next
    ) =>
        context =>
        {
            var info = context.OperationDocumentInfo;
            if (
                info.Document is not null
                && !info.Id.IsEmpty
                && !info.Hash.IsEmpty
                && !string.Equals(info.Id.Value, info.Hash.Value, StringComparison.Ordinal)
            )
                info.OperationId = $"{context.GetOperationId()}#{info.Hash.Value}";

            return next(context);
        };
}
