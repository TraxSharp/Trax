using System.Globalization;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.Tests.Fakes;

public sealed class GatherSources : EffectJunction<ResumableInput, Sources>
{
    public override Task<Sources> Run(ResumableInput input)
    {
        ResumeProbe.Note(nameof(GatherSources));
        return Task.FromResult(new Sources(input.Topic));
    }
}
