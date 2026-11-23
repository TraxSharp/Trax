using System.Text.Json.Nodes;
using Trax.Samples.Board.Tickets;

namespace Trax.Samples.Board.Trains.Checks;

public sealed record ReviewCheck(string TicketId, string Decision, string? Reason)
{
    public static ReviewCheck From(JsonObject ctx) =>
        new(ctx.String("ticketId"), ctx.String("decision"), ctx["reason"]?.GetValue<string>());
}
