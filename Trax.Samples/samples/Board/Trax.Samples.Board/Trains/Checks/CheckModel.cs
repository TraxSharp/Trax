using System.Text.Json.Nodes;
using Trax.Samples.Board.Tickets;

namespace Trax.Samples.Board.Trains.Checks;

/// <summary>
/// What a check found: whether the server accepts the agent's work, and why. An assignment's checking state routes
/// on <see cref="Accepted"/> and copies the rest into its context.
/// </summary>
public sealed record CheckResult
{
    public bool Accepted { get; init; }

    public string Message { get; init; } = "";

    /// <summary>For a repro check: how many cases on the ticket have not shown the bug.</summary>
    public int? FailedCases { get; init; }

    public static CheckResult Accept(string message, int? failedCases = null) =>
        new()
        {
            Accepted = true,
            Message = message,
            FailedCases = failedCases,
        };

    public static CheckResult Refuse(string message, int? failedCases = null) =>
        new()
        {
            Accepted = false,
            Message = message,
            FailedCases = failedCases,
        };
}

// Each check's input is built on the server from the assignment's context, never from anything the client sends
// alongside it.

public sealed record TriageCheck(string TicketId, string Area)
{
    public static TriageCheck From(JsonObject ctx) =>
        new(ctx.String("ticketId"), ctx.String("area"));
}

public sealed record ReproCheck(string TicketId, decimal Subtotal, decimal ExpectedShipping)
{
    public static ReproCheck From(JsonObject ctx) =>
        new(ctx.String("ticketId"), ctx.Number("subtotal"), ctx.Number("expectedShipping"));
}

public sealed record CloseCheck(string TicketId)
{
    public static CloseCheck From(JsonObject ctx) => new(ctx.String("ticketId"));
}

public sealed record FixCheck(string TicketId, decimal FreeFrom, bool Inclusive)
{
    public static FixCheck From(JsonObject ctx) =>
        new(
            ctx.String("ticketId"),
            ctx.Number("freeFrom"),
            ctx["inclusive"]?.GetValue<bool>() ?? false
        );
}

public sealed record QaCheck(string TicketId, IReadOnlyList<decimal> Subtotals)
{
    public static QaCheck From(JsonObject ctx) =>
        new(
            ctx.String("ticketId"),
            ctx["subtotals"]?.AsArray().Select(s => s!.GetValue<decimal>()).ToList() ?? []
        );
}

public sealed record ReviewCheck(string TicketId, string Decision, string? Reason)
{
    public static ReviewCheck From(JsonObject ctx) =>
        new(ctx.String("ticketId"), ctx.String("decision"), ctx["reason"]?.GetValue<string>());
}
