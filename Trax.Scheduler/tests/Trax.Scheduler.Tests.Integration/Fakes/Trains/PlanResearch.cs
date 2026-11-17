using System.Collections.Concurrent;
using System.Globalization;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Fakes.Trains;

public sealed class PlanResearch : EffectJunction<ResearchInput, ResearchBrief>
{
    public override async Task<ResearchBrief> Run(ResearchInput input)
    {
        await ResearchProbe.Note(input.Topic, nameof(PlanResearch)).ConfigureAwait(false);
        return new ResearchBrief(input.Topic);
    }
}
