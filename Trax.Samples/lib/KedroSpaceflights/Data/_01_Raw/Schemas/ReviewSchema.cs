using Flowthru.Abstractions;

namespace KedroSpaceflights.Data._01_Raw.Schemas;

/// <summary>
/// Represents raw review data as imported from text files.
/// All fields are stored as strings pending parsing.
/// </summary>
[FlowthruSchema]
public partial record ReviewSchema
{
    /// <summary>
    /// Identifier of the shuttle being reviewed.
    /// </summary>
    [SerializedLabel("shuttle_id")]
    public string ShuttleId { get; init; } = null!;

    /// <summary>
    /// Review rating score as a string.
    /// </summary>
    [SerializedLabel("review_scores_rating")]
    public string ReviewScoresRating { get; init; } = null!;
}
