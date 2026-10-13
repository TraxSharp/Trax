using System.Collections.Immutable;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.PersistedOperations.Services;
using Trax.Api.GraphQL.PersistedOperations.Storage;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Api.GraphQL.PersistedOperations.Middleware;

/// <summary>
/// Answers a request naming a machine document this node does not serve, before anything is looked up: an older
/// version's id, or one no version generated, with <c>client-outdated</c> (the client reloads), and a newer version's
/// with <c>server-outdated</c> (the client retries, and may reach a node that runs it). A node runs only its own
/// machine documents, so a client built against one version never runs another version's machine.
/// <para>
/// Neither answer is a 5xx: <c>client-outdated</c> is 400 and <c>server-outdated</c> 409. Anyone can send a
/// well-formed id under the reserved prefix, so an answer a load balancer counts as a server failure would let any
/// caller have healthy nodes ejected; the client reads the code, not the status, to decide to retry.
/// </para>
/// </summary>
/// <remarks>
/// It runs before the document cache, so neither answer is ever cached, and a request that carries a document of its
/// own is left to <see cref="PersistedOperationIdBindingMiddleware"/>. See
/// <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.
/// </remarks>
internal static class MachineDocumentMiddleware
{
    public const string Key = "Trax.MachineDocuments";

    internal const string ClientOutdatedMessage =
        "This client was built for another version of the machine than this server runs. Reload it.";

    internal const string ServerOutdatedMessage =
        "This client runs a newer version of the machine than this server does yet. Retry: another server may run it.";

    /// <summary>
    /// The HTTP status of a <c>server-outdated</c> answer: 409 Conflict, never a 5xx that outlier detection would count
    /// against a healthy node.
    /// </summary>
    internal const int ServerOutdatedStatus = 409;

    public static RequestDelegate Create(
        RequestMiddlewareFactoryContext factory,
        RequestDelegate next
    )
    {
        var registry = factory.Services.GetRequiredService<MachineDocumentRegistry>();
        var storage = factory.Services.GetRequiredService<DbPersistedOperationStorage>();

        return context => InvokeAsync(context, registry, storage, next);
    }

    private static async ValueTask InvokeAsync(
        RequestContext context,
        MachineDocumentRegistry registry,
        DbPersistedOperationStorage storage,
        RequestDelegate next
    )
    {
        var request = context.Request;
        if (
            request.DocumentId.IsEmpty
            || request.Document is not null
            || !MachineDocuments.IsReserved(request.DocumentId.Value)
        )
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        switch (
            await registry
                .ClassifyAsync(
                    request.DocumentId.Value,
                    storage.ReadMachineDocumentVersionsAsync,
                    context.RequestAborted
                )
                .ConfigureAwait(false)
        )
        {
            case MachineDocumentVerdict.Own:
                await next(context).ConfigureAwait(false);
                return;
            case MachineDocumentVerdict.ServerOutdated:
                context.Result = Refusal(
                    DraftRefusalCodes.ServerOutdated,
                    ServerOutdatedMessage,
                    ServerOutdatedStatus
                );
                return;
            default:
                context.Result = Refusal(
                    DraftRefusalCodes.ClientOutdated,
                    ClientOutdatedMessage,
                    400
                );
                return;
        }
    }

    private static OperationResult Refusal(string code, string message, int status) =>
        new(
            ImmutableList.Create(ErrorBuilder.New().SetMessage(message).SetCode(code).Build()),
            null
        )
        {
            ContextData = ImmutableDictionary<string, object?>.Empty.Add(
                ExecutionContextData.HttpStatusCode,
                status
            ),
        };
}
