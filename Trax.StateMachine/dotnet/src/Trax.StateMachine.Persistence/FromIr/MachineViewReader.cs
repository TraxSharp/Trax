using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Persistence.FromIr;

/// <summary>
/// Reads one view for one caller: the caller's newest instances of each user machine it reads, at most a limit of them,
/// newest last, and the system
/// instance of each system machine at its key, never anyone else's. Each instance is its id and the view's fields of that
/// machine: a state as its label when the view gives one, a context field as stored. The view's read rule is checked by
/// the caller of this, before it reads; each user machine's own read rule is checked here, as its <c>get</c> checks it,
/// and a machine that refuses the caller reads as no instances. A system machine declares no read rule of its own.
/// </summary>
[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
    System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All
)]
internal static class MachineViewReader
{
    /// <summary>
    /// The view as JSON: one key per machine it reads, a list of at most <paramref name="rowLimit"/> rows for a user
    /// machine and one row (or null) for a system machine.
    /// </summary>
    internal static async Task<JsonObject> Read(
        IDataContext context,
        HostView view,
        Actor.Client caller,
        IReadOnlyDictionary<string, IMachine> machines,
        int rowLimit,
        CancellationToken cancellationToken
    )
    {
        var userKey = caller.UserKey;
        var result = new JsonObject();
        foreach (var read in view.Reads)
        {
            var fields = view
                .Fields.Where(f => string.Equals(f.Machine, read.Machine, StringComparison.Ordinal))
                .ToList();
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
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
                result[read.Machine] = row is null
                    ? null
                    : Row(row.Id, row.State, row.Context, fields);
                continue;
            }
            // A machine served some other way than from features has no rules to hold the caller to: it is left out.
            if (
                !machines.TryGetValue(read.Machine, out var machine)
                || machine is not IMachineInternals internals
                || !await CallerRules
                    .MayRead(caller, internals.Rules, cancellationToken)
                    .ConfigureAwait(false)
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
                .OrderByDescending(d => d.RowId)
                .Take(rowLimit)
                .Select(d => new
                {
                    d.Id,
                    d.State,
                    d.Context,
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            // Read newest first to take the newest; listed oldest first, as a view always lists them.
            rows.Reverse();
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
