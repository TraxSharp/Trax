namespace Trax.Samples.Board.Trains.Work;

/// <summary>A tool a worker may call: what an order pays for shipping under a ticket's rules as they are today.</summary>
public sealed record ChargeInput
{
    public string TicketId { get; init; } = "";

    public decimal Subtotal { get; init; }
}
