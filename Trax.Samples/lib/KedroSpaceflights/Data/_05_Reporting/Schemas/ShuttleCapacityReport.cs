using Flowthru.Abstractions;

namespace KedroSpaceflights.Data._05_Reporting.Schemas;

/// <summary>
/// Represents a passenger capacity summary report grouped by shuttle type.
/// </summary>
/// <remarks>
/// Uses required members to enforce that all critical report fields must be set
/// during construction, ensuring complete reporting outputs.
/// </remarks>
[FlowthruSchema]
public partial record ShuttleCapacityReport
{
    /// <summary>
    /// Type or model of the shuttle.
    /// </summary>
    [SerializedLabel("shuttle_type")]
    public required string ShuttleType { get; init; }

    /// <summary>
    /// Average passenger capacity for this shuttle type.
    /// </summary>
    [SerializedLabel("avg_passenger_capacity")]
    public required decimal AvgPassengerCapacity { get; init; }
}
