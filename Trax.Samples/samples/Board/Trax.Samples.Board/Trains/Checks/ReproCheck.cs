using System.Text.Json.Nodes;
using Trax.Samples.Board.Tickets;

namespace Trax.Samples.Board.Trains.Checks;

public sealed record ReproCheck(string TicketId, decimal Subtotal, decimal ExpectedShipping)
{
    public static ReproCheck From(JsonObject ctx) =>
        new(ctx.String("ticketId"), ctx.Number("subtotal"), ctx.Number("expectedShipping"));
}
