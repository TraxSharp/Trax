namespace Trax.Samples.Recovery.Trains.ChangeCaseData;

public record ChangeCaseDataInput
{
    public required string RunId { get; init; }
}

public record ChangeCaseDataOutput
{
    public required string RunId { get; init; }
    public required string Change { get; init; }
}
