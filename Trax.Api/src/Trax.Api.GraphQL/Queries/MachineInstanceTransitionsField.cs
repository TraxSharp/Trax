using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.DTOs;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.GraphQL.Queries;

/// <summary>
/// <c>operations.machineInstance { transitions(first:, after:) }</c>: one instance's history, newest first, read
/// through <see cref="IOperationsService.GetMachineInstanceTransitionsAsync"/>, the call the dashboard's instance page
/// makes (central <c>docs/0022</c>). A field of the instance rather than of <c>operations</c>, so a lookup that does not
/// select it never reads the table. Reached only through <c>operations</c>, so the operations gate covers it. Never an
/// owner key, a context or a run's token. Registered with the operations queries. See
/// <c>Trax.Effect/docs/adr/0022-a-machine-instance-history-is-written-with-its-snapshot.md</c>.
/// </summary>
internal static class MachineInstanceTransitionsField
{
    /// <summary>The field's name.</summary>
    public const string Field = "transitions";

    /// <summary>The field, added to <see cref="MachineInstanceDetail"/>.</summary>
    public static ObjectTypeExtension<MachineInstanceDetail> Extension() =>
        new(d =>
            d.Field(Field)
                .Description(
                    "The instance's history, newest first: one transition for every snapshot write Trax recorded, "
                        + "with the command, the states, who made it (never which user), when, and the run or child "
                        + "it started. Pass nextCursor as after to read older ones."
                )
                .Argument(
                    "first",
                    a =>
                        a.Type<NonNullType<IntType>>()
                            .DefaultValue(25)
                            .Description("The page size, clamped to 1 through 500.")
                )
                .Argument(
                    "after",
                    a =>
                        a.Type<LongType>()
                            .Description(
                                "Only transitions older than this revision: a page's nextCursor."
                            )
                )
                .Type<NonNullType<ObjectType<MachineInstanceTransitions>>>()
                .Resolve(async ctx =>
                {
                    var instance = ctx.Parent<MachineInstanceDetail>();
                    var page = await ctx
                        .Services.GetRequiredService<IOperationsService>()
                        .GetMachineInstanceTransitionsAsync(
                            new MachineInstanceKey(
                                instance.Machine,
                                instance.OwnerKind,
                                instance.Id,
                                instance.RowId
                            ),
                            ctx.ArgumentValue<long?>("after"),
                            OperationsPageBounds.Take(ctx.ArgumentValue<int>("first")),
                            ctx.RequestAborted
                        );
                    return MachineInstanceTransitions.From(page);
                })
        );
}
