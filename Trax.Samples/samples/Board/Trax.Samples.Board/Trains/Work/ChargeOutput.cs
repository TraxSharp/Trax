namespace Trax.Samples.Board.Trains.Work;

public sealed record ChargeOutput
{
    public decimal Subtotal { get; init; }

    public decimal Shipping { get; init; }
}
