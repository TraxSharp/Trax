using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.Auth;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.Services.BulkOperations;

namespace Trax.Api.GraphQL.Mutations;

// A bulk operation applies one action to every row a list filter matches, in the background, in
// batches through the shared operation the id-list mutation of the same kind calls (central
// docs/0022). It sits under the operations field, so the host's operations gate decides who may
// start one. See docs/adr/0045-a-bulk-operation-applies-to-a-confirmed-snapshot-of-a-filter.md.
/// <seealso href="https://traxsharp.net/docs/sdk-reference/graphql-api/mutations/bulk-operations">SDK reference</seealso>
public partial class OperationsMutations
{
    /// <summary>
    /// Applies <c>kind</c> to every run, dead letter or manifest <c>filter</c> matches, the filter
    /// the list read of that kind takes: <c>CANCEL</c> to runs (<c>filter.executions</c>),
    /// <c>REQUEUE</c> and <c>ACKNOWLEDGE</c> to dead letters (<c>filter.deadLetters</c>), and
    /// <c>TRIGGER</c>, <c>ENABLE</c> and <c>DISABLE</c> to manifests (<c>filter.manifests</c>).
    /// The matching ids are read once, now. When there are not exactly <c>expectedCount</c> of
    /// them, the number the operator confirmed, nothing starts and <c>matched</c> is the number
    /// there are. Otherwise the operation starts in the background on this node and is returned at
    /// once; read <c>operations.bulkOperation(id)</c> until its status is no longer
    /// <c>RUNNING</c>. It runs in batches of at most 500 ids, each through the operation the
    /// matching id-list mutation calls (<c>cancelExecutions</c>, <c>requeueDeadLetters</c>,
    /// <c>acknowledgeDeadLetters</c>, <c>triggerManifests</c>, <c>setManifestsEnabled</c>), and
    /// records each id that did not take with its reason. There is no limit on how many ids it
    /// matches. <c>askAfresh</c> applies to <c>REQUEUE</c> and <c>TRIGGER</c> only, and
    /// <c>note</c>, required, to <c>ACKNOWLEDGE</c> only. One runs per node at a time: while one
    /// runs, this starts nothing and returns that one. The caller is recorded as its
    /// <c>actor</c>, and when the host audits the API (<c>AddAudit</c>) its outcome is written to
    /// the audit too.
    /// </summary>
    /// <param name="kind">What to do to each id.</param>
    /// <param name="filter">Which list, filtered as its read filters it.</param>
    /// <param name="expectedCount">How many matches the operator confirmed.</param>
    /// <param name="applicationServices">Resolved from DI; not a GraphQL argument.</param>
    /// <param name="user">The caller; not a GraphQL argument.</param>
    /// <param name="ct">Cancels the snapshot; the operation, once started, is not tied to it.</param>
    /// <param name="askAfresh">Every requeued or triggered run asks its deciders afresh.</param>
    /// <param name="note">Why the dead letters are acknowledged; at most 1,000 characters.</param>
    public async Task<StartBulkOperationResponse> StartBulkOperation(
        BulkOperationKind kind,
        BulkOperationFilter filter,
        int expectedCount,
        [Service] TraxApplicationServices applicationServices,
        [GlobalState(nameof(ClaimsPrincipal))] ClaimsPrincipal? user,
        CancellationToken ct,
        bool askAfresh = false,
        string? note = null
    ) =>
        await applicationServices
            .Services.GetRequiredService<BulkOperationJobs>()
            .StartAsync(
                new BulkOperationRequest(kind, filter, expectedCount, askAfresh, note),
                ActorOf(user),
                ct
            );

    /// <summary>
    /// Who the caller is, as a bulk operation records it: the Trax principal id when the caller
    /// carries one, else the name it signed in with, else <c>&lt;anonymous&gt;</c>.
    /// </summary>
    internal static BulkOperationActor ActorOf(ClaimsPrincipal? user)
    {
        if (user is not null && user.TryGetPrincipalId(out var id))
            return new BulkOperationActor(
                id,
                id,
                user.FindFirst(TraxAuthClaimTypes.PrincipalType)?.Value
            );

        var name = user?.Identity is { IsAuthenticated: true, Name: { Length: > 0 } signedIn }
            ? signedIn
            : null;
        return new BulkOperationActor(name ?? BulkOperationActor.Anonymous, null, null);
    }
}
