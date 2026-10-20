// The trains the skill's machine invokes, which the skill names but does not show.
using Trax.Effect.Services.ServiceTrain;

public sealed record LoadRunInput(string RunId);

public sealed record LoadRunOutput
{
    public int Version { get; init; }
}

public interface ILoadRunTrain : IServiceTrain<LoadRunInput, LoadRunOutput>;

public sealed record PlayCardInput(string RunId, int Version, int Slot, int Target);

public sealed record PlayCardOutput
{
    public int Version { get; init; }

    public bool Finished { get; init; }
}

public interface IPlayCardTrain : IServiceTrain<PlayCardInput, PlayCardOutput>;
