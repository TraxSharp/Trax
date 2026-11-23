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

public sealed class SearchDeep : EffectJunction<ResearchBrief, ResearchFindings>
{
    public override async Task<ResearchFindings> Run(ResearchBrief input)
    {
        await ResearchProbe.Note(input.Topic, nameof(SearchDeep)).ConfigureAwait(false);
        return new ResearchFindings(input.Topic, "deep");
    }
}
