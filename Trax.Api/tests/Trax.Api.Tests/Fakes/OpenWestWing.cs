using System.Globalization;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.Tests.Fakes;

public sealed class OpenWestWing : EffectJunction<Sources, Sources>
{
    public override Task<Sources> Run(Sources input)
    {
        ResumeProbe.Note(nameof(OpenWestWing));
        return Task.FromResult(input);
    }
}
