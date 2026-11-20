using System.Text.Json.Nodes;
using Trax.Core.Extensions;
using Trax.Samples.Board.Tickets;

namespace Trax.Samples.Board.Trains.Checks;

public sealed record QaCheck(string TicketId, IReadOnlyList<decimal> Subtotals)
{
    public static QaCheck From(JsonObject ctx) =>
        new(
            ctx.String("ticketId"),
            ctx["subtotals"]?.AsArray().Select(s => s.NotNull().GetValue<decimal>()).ToList() ?? []
        );
}
