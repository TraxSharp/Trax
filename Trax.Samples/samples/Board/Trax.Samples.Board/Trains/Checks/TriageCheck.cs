using System.Text.Json.Nodes;
using Trax.Samples.Board.Tickets;

namespace Trax.Samples.Board.Trains.Checks;

// Each check's input is built on the server from the assignment's context, never from anything the client sends
// alongside it.

public sealed record TriageCheck(string TicketId, string Area)
{
    public static TriageCheck From(JsonObject ctx) =>
        new(ctx.String("ticketId"), ctx.String("area"));
}
