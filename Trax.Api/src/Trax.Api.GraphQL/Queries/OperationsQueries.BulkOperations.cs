using Microsoft.Extensions.DependencyInjection;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.Services.BulkOperations;

namespace Trax.Api.GraphQL.Queries;

// The bulk operations startBulkOperation started on this node. Both read the node's memory, never
// a table, and sit under the operations field, so the host's operations gate decides who reads them.
// See docs/adr/0045-a-bulk-operation-applies-to-a-confirmed-snapshot-of-a-filter.md.
/// <seealso href="https://traxsharp.net/docs/sdk-reference/graphql-api/mutations/bulk-operations">SDK reference</seealso>
public partial class OperationsQueries
{
    /// <summary>
    /// Every bulk operation this node knows, newest first: the one running, and those that
    /// finished in the last 24 hours (at most 100). An operation started on another node, or
    /// before this node restarted, is not listed.
    /// </summary>
    /// <param name="applicationServices">Resolved from DI; not a GraphQL argument.</param>
    public IReadOnlyList<BulkOperation> GetBulkOperations(
        [Service] TraxApplicationServices applicationServices
    ) => applicationServices.Services.GetRequiredService<BulkOperationJobs>().List();

    /// <summary>
    /// One bulk operation by id, or <c>null</c> when this node does not know it: it was started on
    /// another node, this node has restarted since, or it finished more than 24 hours ago.
    /// </summary>
    /// <param name="id">The operation's id, as <c>startBulkOperation</c> returned it.</param>
    /// <param name="applicationServices">Resolved from DI; not a GraphQL argument.</param>
    public BulkOperation? GetBulkOperation(
        Guid id,
        [Service] TraxApplicationServices applicationServices
    ) => applicationServices.Services.GetRequiredService<BulkOperationJobs>().Get(id);
}
