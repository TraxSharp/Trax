using System.Globalization;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.Tests.Fakes;

public sealed class FetchSources : EffectJunction<Sources, FetchedSources>
{
    public override Task<FetchedSources> Run(Sources input)
    {
        ResumeProbe.Note(nameof(FetchSources));
        return Task.FromResult(new FetchedSources(input.Topic, 12, ResumeProbe.StateSecret));
    }
}
