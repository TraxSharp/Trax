using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Persistence.FromIr;

/// <summary>
/// The views a host serves from features (ADR <c>Trax.Docs/adr/0058</c>), by name, as its host file declares them:
/// what each reads, the fields it shows and who may read it. The GraphQL surface serves one query field per view.
/// </summary>
internal sealed class MachineViewCatalog(IReadOnlyDictionary<string, HostView> views)
{
    internal IReadOnlyDictionary<string, HostView> Views { get; } = views;
}

/// <summary>
/// Reads one view for one caller: the caller's own instances of each user machine it reads, newest last, and the system
/// instance of each system machine at its key, never anyone else's. Each instance is its id and the view's fields of that
/// machine: a state as its label when the view gives one, a context field as stored. The view's read rule is checked by
/// the caller of this, before it reads; each user machine's own read rule is checked here, as its <c>get</c> checks it,
/// and a machine that refuses the caller reads as no instances. A system machine declares no read rule of its own.
/// </summary>
internal static class MachineViewReader
{
    /// <summary>
    /// The view as JSON: one key per machine it reads, a list of rows for a user machine and one row (or null) for a
    /// system machine.
    /// </summary>
    internal static async Task<JsonObject> Read(
        IDataContext context,
        HostView view,
        Actor.Client caller,
        IReadOnlyDictionary<string, IMachine> machines,
        CancellationToken cancellationToken
    )
    {
        var userKey = caller.UserKey;
        var result = new JsonObject();
        foreach (var read in view.Reads)
        {
            var fields = view.Fields.Where(f => f.Machine == read.Machine).ToList();
            if (read.Key is { } key)
            {
                var id = SystemInstanceStarter.IdOf(read.Machine, key);
                var row = await context
                    .SnapshotDrafts.AsNoTracking()
                    .Where(d =>
                        d.Id == id
                        && d.Machine == read.Machine
                        && d.OwnerKind == SnapshotOwnerKind.System
                    )
                    .Select(d => new
                    {
                        d.Id,
                        d.State,
                        d.Context,
                    })
                    .FirstOrDefaultAsync(cancellationToken);
                result[read.Machine] = row is null
                    ? null
                    : Row(row.Id, row.State, row.Context, fields);
                continue;
            }
            // A machine served some other way than from features has no rules to hold the caller to: it is left out.
            if (
                !machines.TryGetValue(read.Machine, out var machine)
                || machine is not IMachineInternals internals
                || !await CallerRules.MayRead(caller, internals.Rules, cancellationToken)
            )
            {
                result[read.Machine] = new JsonArray();
                continue;
            }
            var rows = await context
                .SnapshotDrafts.AsNoTracking()
                .Where(d =>
                    d.Machine == read.Machine
                    && d.OwnerKind == SnapshotOwnerKind.User
                    && d.UserKey == userKey
                )
                .OrderBy(d => d.RowId)
                .Select(d => new
                {
                    d.Id,
                    d.State,
                    d.Context,
                })
                .ToListAsync(cancellationToken);
            result[read.Machine] = new JsonArray(
                rows.Select(r => (JsonNode?)Row(r.Id, r.State, r.Context, fields)).ToArray()
            );
        }
        return result;
    }

    private static JsonObject Row(
        Guid id,
        string state,
        string stored,
        IReadOnlyList<ViewField> fields
    )
    {
        var context = JsonNode.Parse(stored) as JsonObject ?? [];
        var row = new JsonObject { ["id"] = id.ToString() };
        foreach (var field in fields)
            row[field.Name] = field.IsState
                ? field.Labels?.GetValueOrDefault(state) ?? state
                : context[field.Source]?.DeepClone();
        return row;
    }
}
