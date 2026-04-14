namespace Trax.Samples.JobHunt.Trains.GetArtifacts;

public record ArtifactSummary
{
    public Guid Id { get; init; }
    public required string Type { get; init; }
    public required string Content { get; init; }
    public required string ModelUsed { get; init; }
    public DateTime GeneratedAt { get; init; }
}

public record GetArtifactsOutput
{
    public required List<ArtifactSummary> Artifacts { get; init; }
}
