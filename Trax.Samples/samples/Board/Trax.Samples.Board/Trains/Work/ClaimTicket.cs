using System.Globalization;
using System.Xml;
using Trax.Api.Auth;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Board.Auth;
using Trax.Samples.Board.Machines;
using Trax.Samples.Board.Tickets;
using Trax.StateMachine.Persistence;

namespace Trax.Samples.Board.Trains.Work;

public class ClaimTicket(TraxPrincipal caller, TicketBoard board)
    : Junction<ClaimWorkInput, WorkPackage>
{
    public override async Task<WorkPackage> Run(ClaimWorkInput input)
    {
        var lane = Lanes.Find(input.Lane);
        if (lane is null)
            return Refused(input.Lane, "NOTHING_TO_CLAIM", $"there is no lane {input.Lane}");
        if (!caller.Roles.Contains(lane.Role, StringComparer.Ordinal))
            return Refused(
                lane.Key,
                "FORBIDDEN",
                $"claiming from {lane.Key} needs the {lane.Role} role"
            );

        var tickets = await board.All(CancellationToken).ConfigureAwait(false);
        var ticket = input.TicketId is { } id
            ? tickets.FirstOrDefault(t =>
                string.Equals(t.Id, id, global::System.StringComparison.Ordinal)
            )
            : tickets.FirstOrDefault(t =>
                t.State == lane.State && (t.Context.ClaimedBy is null || TicketBoard.Lapsed(t))
            );
        if (ticket is null)
            return Refused(
                lane.Key,
                "NOTHING_TO_CLAIM",
                input.TicketId is { } missing
                    ? $"no ticket {missing}"
                    : $"no free ticket in {lane.Key}"
            );
        if (ticket.State != lane.State)
            return Refused(
                lane.Key,
                "NOTHING_TO_CLAIM",
                $"{ticket.Id} is in {ticket.State}, not in {lane.Key}"
            );
        if (
            string.Equals(ticket.Context.ClaimedBy, caller.Id, StringComparison.Ordinal)
            && !TicketBoard.Lapsed(ticket)
        )
            return Package(lane, ticket);
        if (ticket.Context.ClaimedBy is not null && TicketBoard.Lapsed(ticket))
            await board
                .Fire(ticket.Id, TicketTrigger.Release, null, CancellationToken)
                .ConfigureAwait(false);

        var claimed = await board
            .Fire(
                ticket.Id,
                TicketTrigger.Claim,
                new ClaimInput
                {
                    By = caller.Id,
                    At = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                },
                CancellationToken
            )
            .ConfigureAwait(false);
        return claimed switch
        {
            AdvanceOutcome.Advanced => Package(
                lane,
                (await board.Find(ticket.Id, CancellationToken).ConfigureAwait(false))!
            ),
            AdvanceOutcome.Rejected or AdvanceOutcome.Conflict => Refused(
                lane.Key,
                "ALREADY_CLAIMED",
                $"{ticket.Id} is claimed by another worker"
            ),
            _ => Refused(lane.Key, "NOTHING_TO_CLAIM", TicketBoard.Why(claimed)),
        };
    }

    private static WorkPackage Package(LaneSpec lane, Ticket ticket) =>
        new()
        {
            Ticket = Lanes.View(ticket),
            Lane = lane.Key,
            Instructions = lane.Instructions,
            Tools = lane.Tools,
            Events = lane.Events,
            Deadline = XmlConvert.ToString(TicketBoard.Lease),
        };

    private static WorkPackage Refused(string lane, string code, string message) =>
        new()
        {
            Lane = lane,
            Problem = new ClaimProblem { Code = code, Message = message },
        };
}
