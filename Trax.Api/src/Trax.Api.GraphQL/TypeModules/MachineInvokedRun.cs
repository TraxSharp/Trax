using System.Text.Json.Nodes;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Api.GraphQL.TypeModules;

/// <summary>
/// <c>invokedRun { runId }</c> on the answers of a machine that invokes a train: the external id of the run the
/// instance's current state queued, while the instance sits in that state, so its owner can follow the run through
/// <c>runProgress</c>. Null in any other state, on a refusal, and for anyone but the instance's user owner. Read only
/// when selected, through one query on the instance's row.
/// </summary>
internal static class MachineInvokedRun
{
    /// <summary>The GraphQL type of the field.</summary>
    public const string TypeName = "MachineInvokedRun";

    /// <summary>Whether <paramref name="machine"/> declares a state that invokes a train.</summary>
    public static bool Invokes(IMachine machine) =>
        JsonNode.Parse(machine.ExportIr()) is JsonObject ir && ir.ContainsKey("outcomes");

    /// <summary>The field's type, added once when any exposed machine invokes a train.</summary>
    public static ObjectType Type() =>
        new(d =>
        {
            d.Name(TypeName);
            d.Description("The run a machine instance's current state queued.");
            d.Field("runId")
                .Type<NonNullType<StringType>>()
                .Description("The run's external id: follow it with runProgress.")
                .Resolve(ctx => ctx.Parent<string>());
        });

    /// <summary>Adds the field to the result type of <paramref name="machine"/>.</summary>
    public static void Add(IObjectTypeDescriptor d, IMachine machine) =>
        d.Field("invokedRun")
            .Type(new HotChocolate.Language.NamedTypeNode(TypeName))
            .Description(
                "The run this instance's current state queued, while it sits in that state; null otherwise. Only "
                    + "the instance's owner sees it."
            )
            .Resolve(ctx => ResolveAsync(ctx, machine.Name));

    private static async Task<string?> ResolveAsync(IResolverContext ctx, string machine)
    {
        if (ctx.Parent<MachineAnswer>() is not { Problem: null, Id: { } id })
            return null;
        if (ctx.Services.GetService<ISnapshotPrincipal>()?.CurrentUserKey is not { } userKey)
            return null;

        using var db = await ctx
            .Services.GetRequiredService<IDataContextProviderFactory>()
            .CreateDbContextAsync(ctx.RequestAborted)
            .ConfigureAwait(false);
        return await db
            .SnapshotDrafts.AsNoTracking()
            .Where(x =>
                x.Id == id
                && x.Machine == machine
                && x.OwnerKind == SnapshotOwnerKind.User
                && x.UserKey == userKey
            )
            .Select(x => x.InvokeToken)
            .FirstOrDefaultAsync(ctx.RequestAborted)
            .ConfigureAwait(false);
    }
}
