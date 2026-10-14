// The train the page's machine invokes, which the page describes in prose rather than showing.
using Trax.Effect.Services.ServiceTrain;

public sealed record FetchInput(string Source);

public sealed record FetchOutput
{
    public bool Unsure { get; init; }

    public string Fingerprint { get; init; } = "";
}

public interface IFetchTrain : IServiceTrain<FetchInput, FetchOutput>;
