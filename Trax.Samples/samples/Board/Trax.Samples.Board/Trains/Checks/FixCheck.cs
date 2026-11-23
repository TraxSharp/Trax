using System.Text.Json.Nodes;
using Trax.Samples.Board.Tickets;

namespace Trax.Samples.Board.Trains.Checks;

public sealed record FixCheck(string TicketId, decimal FreeFrom, bool Inclusive)
{
    public static FixCheck From(JsonObject ctx) =>
        new(
            ctx.String("ticketId"),
            ctx.Number("freeFrom"),
            ctx["inclusive"]?.GetValue<bool>() ?? false
        );
}
