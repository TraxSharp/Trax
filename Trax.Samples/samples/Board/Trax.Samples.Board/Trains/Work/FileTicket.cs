using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Board.Auth;
using Trax.Samples.Board.Shop;
using Trax.Samples.Board.Tickets;

namespace Trax.Samples.Board.Trains.Work;

public class FileTicket(TicketBoard board) : Junction<FileTicketInput, TicketView>
{
    public override async Task<TicketView> Run(FileTicketInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Id) || input.Id.Length > 40)
            throw new ArgumentException("A ticket id is 1 to 40 characters.", nameof(input));
        if (!Reports.All.TryGetValue(input.Report, out var report))
            throw new ArgumentException(
                $"There is no report {input.Report}; the shop knows {string.Join(", ", Reports.All.Keys)}.",
                nameof(input)
            );
        return Lanes.View(
            await board.File(input.Id, report, CancellationToken).ConfigureAwait(false)
        );
    }
}
