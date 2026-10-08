using Trax.Api.DTOs;
using Trax.Effect.Enums;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.GraphQL.Queries;

// The operator's read-only view of state-machine instances. Each field reads through
// IOperationsService, the calls the dashboard's State machines page makes (central docs/0022),
// and sits under the operations field, so the host's operations gate decides who reads it. No
// field returns a snapshot's context or its owner's key (central docs/0046).
public partial class OperationsQueries
{
    /// <summary>The error code a user's draft looked up without its row id is refused with.</summary>
    internal const string RowIdRequiredCode = "TRAX_ROW_ID_REQUIRED";

    /// <summary>
    /// A page of state-machine instances, system-owned and user-owned alike, newest first by when
    /// each was last written. Each carries its machine, owner kind, id, state, version,
    /// timestamps and whether it waits on a train run it invoked; never its context or its
    /// owner's key. Page with <c>skip</c> and <c>take</c>; there is no cursor, because the order
    /// is by a time that moves. The total counts at most 10,000 instances: past that it reads
    /// 10,000 with <c>isCountCapped</c> true, and <c>machineInstanceCounts</c> has the exact
    /// numbers by state.
    /// </summary>
    /// <param name="operationsService">Resolved from DI; not a GraphQL argument.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <param name="machine">Only instances of this machine (its id, matched exactly).</param>
    /// <param name="state">Only instances in this state (matched exactly).</param>
    /// <param name="ownerKind">Only instances a user owns, or only those the system owns.</param>
    /// <param name="skip">How many instances to skip (negative is treated as 0; above 10,000 is refused with <c>TRAX_SKIP_TOO_DEEP</c>).</param>
    /// <param name="take">The page size, clamped to 1 through 500.</param>
    public async Task<PagedResult<MachineInstance>> GetMachineInstances(
        [Service] IOperationsService operationsService,
        CancellationToken ct,
        string? machine = null,
        string? state = null,
        SnapshotOwnerKind? ownerKind = null,
        int skip = 0,
        int take = 25
    )
    {
        var query = new MachineInstanceQuery(
            machine,
            state,
            ownerKind,
            OperationsPageBounds.Skip(skip),
            OperationsPageBounds.Take(take)
        );

        var page = await operationsService.GetMachineInstancesAsync(query, ct);
        var total = await operationsService.CountMachineInstancesAsync(query, ct);

        return new PagedResult<MachineInstance>(
            page.Items.Select(MachineInstance.From).ToList(),
            total.Count,
            page.Skip,
            page.Take
        )
        {
            IsCountCapped = total.Capped,
        };
    }

    /// <summary>
    /// One state-machine instance, or null when none matches. The owner kind is always named,
    /// because a user can hold a draft under the same id as a system instance. A system instance
    /// is unique by machine and id; a user's draft is named by its <c>rowId</c> too (refused with
    /// <c>TRAX_ROW_ID_REQUIRED</c> without one), because several users can each hold a draft
    /// under one id and an operator is not shown whose a draft is.
    /// </summary>
    /// <param name="machine">The machine's id.</param>
    /// <param name="ownerKind">Who owns the instance.</param>
    /// <param name="id">The instance or draft id.</param>
    /// <param name="operationsService">Resolved from DI; not a GraphQL argument.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <param name="rowId">The row's key, from the list; required for a user's draft.</param>
    public async Task<MachineInstanceDetail?> GetMachineInstance(
        string machine,
        SnapshotOwnerKind ownerKind,
        Guid id,
        [Service] IOperationsService operationsService,
        CancellationToken ct,
        long? rowId = null
    )
    {
        if (string.IsNullOrWhiteSpace(machine))
            throw new GraphQLException(
                ErrorBuilder
                    .New()
                    .SetMessage("machine must name a machine.")
                    .SetCode(Validation.RunIdArgument.ErrorCode)
                    .Build()
            );
        if (ownerKind == SnapshotOwnerKind.User && rowId is null)
            throw new GraphQLException(
                ErrorBuilder
                    .New()
                    .SetMessage(
                        "A user's draft is named by its rowId as well as its machine and id: "
                            + "several users can each hold a draft under one id. Pass the rowId "
                            + "machineInstances gives it."
                    )
                    .SetCode(RowIdRequiredCode)
                    .Build()
            );

        var record = await operationsService.GetMachineInstanceAsync(
            new MachineInstanceKey(machine, ownerKind, id, rowId),
            ct
        );
        return record is null ? null : MachineInstanceDetail.From(record);
    }

    /// <summary>
    /// How many instances each machine has in each state, for each owner kind, ordered by machine,
    /// state and owner kind. Exact. Read through the call the dashboard's State machines page
    /// makes for its counts.
    /// </summary>
    /// <param name="operationsService">Resolved from DI; not a GraphQL argument.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <param name="machine">Only this machine's counts; omitted, every machine's.</param>
    public async Task<IReadOnlyList<MachineInstanceCount>> GetMachineInstanceCounts(
        [Service] IOperationsService operationsService,
        CancellationToken ct,
        string? machine = null
    ) =>
        (await operationsService.GetMachineInstanceStateCountsAsync(machine, ct))
            .Select(c => new MachineInstanceCount(c.Machine, c.State, c.OwnerKind, c.Count))
            .ToList();
}
