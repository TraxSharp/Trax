using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Board.Auth;
using Trax.Samples.Board.Shop;
using Trax.Samples.Board.Tickets;

namespace Trax.Samples.Board.Trains.Work;

public class ReadCharge(TicketBoard board) : Junction<ChargeInput, ChargeOutput>
{
    public override async Task<ChargeOutput> Run(ChargeInput input)
    {
        var ticket =
            await board.Find(input.TicketId, CancellationToken).ConfigureAwait(false)
            ?? throw new ArgumentException($"There is no ticket {input.TicketId}.", nameof(input));
        return new ChargeOutput
        {
            Subtotal = input.Subtotal,
            Shipping = ticket.Report.Current.Charge(input.Subtotal),
        };
    }
}
