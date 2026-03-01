using Flowthru.Abstractions;

namespace KedroSpaceflights.Data._01_Raw.Schemas;

/// <summary>
/// Represents raw company data as imported from text files.
/// All fields are stored as strings pending parsing.
/// </summary>
[FlowthruSchema]
public partial record CompanySchema
{
    /// <summary>
    /// Unique identifier for the company.
    /// </summary>
    [SerializedLabel("id")]
    public string Id { get; init; } = null!;

    /// <summary>
    /// Company rating as a percentage string (e.g., "90%").
    /// </summary>
    [SerializedLabel("company_rating")]
    public string CompanyRating { get; init; } = null!;

    /// <summary>
    /// IATA approval status as a string flag ("t" for true, "f" for false).
    /// </summary>
    [SerializedLabel("iata_approved")]
    public string IataApproved { get; init; } = null!;

    /// <summary>
    /// Geographic location of the company.
    /// </summary>
    [SerializedLabel("company_location")]
    public string CompanyLocation { get; init; } = null!;
}
