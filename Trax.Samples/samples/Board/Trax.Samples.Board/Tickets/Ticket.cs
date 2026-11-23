using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Samples.Board.Machines;
using Trax.Samples.Board.Shop;
using Trax.StateMachine.Persistence;

namespace Trax.Samples.Board.Tickets;

/// <summary>A ticket as the board holds it: its state and its context.</summary>
public sealed record Ticket(TicketState State, TicketContext Context)
{
    public string Id => Context.Id;

    public Report Report => Reports.All[Context.Report];

    /// <summary>The lane the ticket is in, or null for Done and Closed.</summary>
    public string? Lane =>
        TicketMachine.Lanes.FirstOrDefault(l => l.Value == State).Key is { } lane ? lane : null;
}
