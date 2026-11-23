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

public sealed class SummarizeLong : EffectJunction<CheckedFindings, string>
{
    public override async Task<string> Run(CheckedFindings input)
    {
        await ResearchProbe.Note(input.Topic, nameof(SummarizeLong)).ConfigureAwait(false);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"long {input.Depth} summary of {input.Topic}, {input.Pages} pages"
        );
    }
}
