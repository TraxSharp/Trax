namespace Trax.Samples.JobHunt.Trains.AddJob;

public record AddJobInput
{
    public required string UserId { get; init; }
    public string? Url { get; init; }
    public string? PastedTitle { get; init; }
    public string? PastedCompany { get; init; }
    public string? PastedDescription { get; init; }
}
