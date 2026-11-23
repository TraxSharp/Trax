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

public sealed class FetchFullTexts : EffectJunction<ResearchFindings, CheckedFindings>
{
    public override async Task<CheckedFindings> Run(ResearchFindings input)
    {
        await ResearchProbe.Note(input.Topic, nameof(FetchFullTexts)).ConfigureAwait(false);
        return new CheckedFindings(input.Topic, input.Depth, 12);
    }
}
