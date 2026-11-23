using System.Text.Json.Nodes;
using Trax.Samples.Board.Tickets;

namespace Trax.Samples.Board.Trains.Checks;

public sealed record CloseCheck(string TicketId)
{
    public static CloseCheck From(JsonObject ctx) => new(ctx.String("ticketId"));
}
