using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Samples.Board.Machines;
using Trax.Samples.Board.Shop;
using Trax.StateMachine.Persistence;

namespace Trax.Samples.Board.Tickets;

/// <summary>A ticket as the board holds it: its state and its context.</summary>
public sealed record Ticket(TicketState State, TicketContext Context)
{
    public string Id => Context.Id;

    public Report Report => Reports.All[Context.Report];

    /// <summary>The lane the ticket is in, or null for Done and Closed.</summary>
    public string? Lane =>
        TicketMachine.Lanes.FirstOrDefault(l => l.Value == State).Key is { } lane ? lane : null;
}

/// <summary>
/// Reads the board and moves tickets on it, as the host. Every train that touches a ticket goes through here, so each
/// move is the ticket machine's own transition, checked by its guards, and nothing else writes a ticket.
/// </summary>
public sealed class TicketBoard(IMachineInstances instances, IDataContextProviderFactory trax)
{
    /// <summary>How long a claim holds a ticket before another worker may take it.</summary>
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(10);

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public async Task<Ticket?> Find(string ticketId, CancellationToken ct)
    {
        var id = TicketMachine.IdFor(ticketId);
        await using var db = await trax.CreateDbContextAsync(ct);
        var row = await db
            .SnapshotDrafts.AsNoTracking()
            .Where(d =>
                d.Machine == TicketMachine.MachineId
                && d.OwnerKind == SnapshotOwnerKind.System
                && d.Id == id
            )
            .Select(d => new { d.State, d.Context })
            .FirstOrDefaultAsync(ct);
        return row is null ? null : Read(row.State, row.Context);
    }

    public async Task<IReadOnlyList<Ticket>> All(CancellationToken ct)
    {
        await using var db = await trax.CreateDbContextAsync(ct);
        var rows = await db
            .SnapshotDrafts.AsNoTracking()
            .Where(d =>
                d.Machine == TicketMachine.MachineId && d.OwnerKind == SnapshotOwnerKind.System
            )
            .Select(d => new { d.State, d.Context })
            .ToListAsync(ct);
        return rows.Select(r => Read(r.State, r.Context))
            .OrderBy(t => t.Id, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<Ticket> File(string ticketId, Report report, CancellationToken ct)
    {
        await instances.Start<TicketMachine>(
            TicketMachine.KeyFor(ticketId),
            TicketMachine.ContextFor(ticketId, report.Key),
            ct
        );
        return (await Find(ticketId, ct))!;
    }

    /// <summary>Fires one of the ticket machine's triggers, as the host.</summary>
    public Task<AdvanceOutcome> Fire(
        string ticketId,
        TicketTrigger trigger,
        object? input,
        CancellationToken ct
    ) =>
        instances.Advance<TicketMachine>(
            TicketMachine.KeyFor(ticketId),
            trigger.ToString(),
            input is null ? null : JsonSerializer.SerializeToNode(input, Web),
            ct
        );

    /// <summary>
    /// Why <paramref name="worker"/> may not act on the ticket in <paramref name="lane"/>, or null when it holds the
    /// ticket there under a lease that has not run out. A lapsed lease is released, so the ticket is free again.
    /// </summary>
    public async Task<string?> RefuseUnlessHeld(
        Ticket? ticket,
        string? worker,
        string lane,
        CancellationToken ct
    )
    {
        if (ticket is null)
            return "no such ticket";
        if (ticket.Lane != lane)
            return $"{ticket.Id} is in {ticket.State}, not in {lane}";
        if (worker is null || ticket.Context.ClaimedBy != worker)
            return $"{ticket.Id} is not claimed by {(worker is null ? "anyone" : Name(worker))}; claim it first";
        if (Lapsed(ticket))
        {
            await Fire(ticket.Id, TicketTrigger.Release, null, ct);
            return $"the claim on {ticket.Id} ran out; claim it again";
        }
        return null;
    }

    /// <summary>
    /// The worker's name, from the key the host knows it by: <c>TraxApiKey:repro-bot</c> is <c>repro-bot</c>. A claim
    /// and an assignment hold the whole key; a note shows the name.
    /// </summary>
    public static string Name(string? principal) =>
        principal is null ? "" : principal[(principal.IndexOf(':') + 1)..];

    public static bool Lapsed(Ticket ticket) =>
        ticket.Context.ClaimedAt is { } at
        && DateTimeOffset.Parse(at, CultureInfo.InvariantCulture) + Lease < DateTimeOffset.UtcNow;

    private static Ticket Read(string state, string context) =>
        new(
            Enum.Parse<TicketState>(state),
            JsonSerializer.Deserialize<TicketContext>(context, Web)
                ?? throw new InvalidOperationException("A ticket row holds no context.")
        );

    /// <summary>The reason an advance did not move the ticket, for a verdict.</summary>
    public static string Why(AdvanceOutcome outcome) =>
        outcome switch
        {
            AdvanceOutcome.Rejected r => r.Detail ?? r.Reason.ToString(),
            AdvanceOutcome.Conflict => "the ticket changed while this ran; send again",
            AdvanceOutcome.NotFound => "no such ticket",
            AdvanceOutcome.LoadError e => e.Message,
            _ => outcome.ToString(),
        };
}

/// <summary>Reads a context the way the machines write it, for code that builds an invoked run's input.</summary>
internal static class ContextReading
{
    public static string String(this JsonObject ctx, string field) =>
        ctx[field]?.GetValue<string>() ?? "";

    public static decimal Number(this JsonObject ctx, string field) =>
        ctx[field]?.GetValue<decimal>() ?? 0m;
}
