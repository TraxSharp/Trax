using Trax.Effect.Models.Manifest;

namespace Trax.Samples.Recovery.Trains.Refund;

/// <summary>
/// The manifest's input: which run, which order. The order's details are read from the order system
/// at run time, so the "change the data" control can change them without touching the input.
/// </summary>
public record RefundInput : IManifestProperties
{
    public required string RunId { get; init; }
    public required string OrderId { get; init; }
}
