using Flowthru.Abstractions;

namespace KedroSpaceflights.Data._03_Primary.Schemas;

/// <summary>
/// Represents a unified model input table combining shuttle, company, and review data.
/// Produced by joining preprocessed shuttle and company data with review scores.
/// </summary>
/// <remarks>
/// Uses required members to enforce that all critical fields must be set
/// during construction, preventing accidental omission in pipeline nodes.
/// </remarks>
[FlowthruSchema]
public partial record ModelInputTableSchema
{
    /// <summary>
    /// Unique identifier for the shuttle.
    /// </summary>
    [SerializedLabel("shuttle_id")]
    public required string ShuttleId { get; init; }

    /// <summary>
    /// Type or model of the shuttle.
    /// </summary>
    [SerializedLabel("shuttle_type")]
    public required string ShuttleType { get; init; }

    /// <summary>
    /// Identifier of the company operating this shuttle.
    /// </summary>
    [SerializedLabel("company_id")]
    public required string CompanyId { get; init; }

    /// <summary>
    /// Number of engines.
    /// </summary>
    [SerializedLabel("engines")]
    public required int Engines { get; init; }

    /// <summary>
    /// Maximum passenger capacity.
    /// </summary>
    [SerializedLabel("passenger_capacity")]
    public required int PassengerCapacity { get; init; }

    /// <summary>
    /// Required crew size.
    /// </summary>
    [SerializedLabel("crew")]
    public required int Crew { get; init; }

    /// <summary>
    /// D-check completion status.
    /// </summary>
    [SerializedLabel("d_check_complete")]
    public required bool DCheckComplete { get; init; }

    /// <summary>
    /// Moon clearance completion status.
    /// </summary>
    [SerializedLabel("moon_clearance_complete")]
    public required bool MoonClearanceComplete { get; init; }

    /// <summary>
    /// Trip price (target variable for prediction).
    /// </summary>
    [SerializedLabel("price")]
    public required decimal Price { get; init; }

    /// <summary>
    /// IATA approval status of the operating company.
    /// </summary>
    [SerializedLabel("iata_approved")]
    public required bool IataApproved { get; init; }

    /// <summary>
    /// Rating of the operating company as a decimal ratio (0.0 to 1.0).
    /// </summary>
    [SerializedLabel("company_rating")]
    public required decimal CompanyRating { get; init; }

    /// <summary>
    /// Review score rating for this shuttle.
    /// </summary>
    [SerializedLabel("review_scores_rating")]
    public required decimal ReviewScoresRating { get; init; }
}
