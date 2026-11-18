using System.Globalization;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.Tests.Fakes;

public sealed class OpenEastWing : EffectJunction<Sources, Sources>
{
    public override Task<Sources> Run(Sources input)
    {
        ResumeProbe.Note(nameof(OpenEastWing));
        return Task.FromResult(input);
    }
}
