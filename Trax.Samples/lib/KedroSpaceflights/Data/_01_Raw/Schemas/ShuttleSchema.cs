using Flowthru.Abstractions;

namespace KedroSpaceflights.Data._01_Raw.Schemas;

/// <summary>
/// Represents raw shuttle data as imported from structured files.
/// All fields are stored as strings pending parsing.
/// </summary>
[FlowthruSchema]
public partial record ShuttleSchema
{
    /// <summary>
    /// Unique identifier for the shuttle.
    /// </summary>
    [SerializedLabel("id")]
    public string Id { get; init; } = null!;

    /// <summary>
    /// Type or model of the shuttle.
    /// </summary>
    [SerializedLabel("shuttle_type")]
    public string ShuttleType { get; init; } = null!;

    /// <summary>
    /// Identifier of the company operating this shuttle.
    /// </summary>
    [SerializedLabel("company_id")]
    public string CompanyId { get; init; } = null!;

    /// <summary>
    /// Number of engines as a string.
    /// </summary>
    [SerializedLabel("engines")]
    public string Engines { get; init; } = null!;

    /// <summary>
    /// Maximum passenger capacity as a string.
    /// </summary>
    [SerializedLabel("passenger_capacity")]
    public string PassengerCapacity { get; init; } = null!;

    /// <summary>
    /// Required crew size as a string.
    /// </summary>
    [SerializedLabel("crew")]
    public string Crew { get; init; } = null!;

    /// <summary>
    /// Trip price as a currency string (e.g., "$1,234.56").
    /// </summary>
    [SerializedLabel("price")]
    public string Price { get; init; } = null!;

    /// <summary>
    /// D-check completion status as a string flag ("t" for true, "f" for false).
    /// </summary>
    [SerializedLabel("d_check_complete")]
    public string DCheckComplete { get; init; } = null!;

    /// <summary>
    /// Moon clearance completion status as a string flag ("t" for true, "f" for false).
    /// </summary>
    [SerializedLabel("moon_clearance_complete")]
    public string MoonClearanceComplete { get; init; } = null!;
}
