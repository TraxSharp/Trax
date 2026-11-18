using System.Globalization;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.Tests.Fakes;

public sealed class SummarizeSources : EffectJunction<FetchedSources, SourceSummary>
{
    public override Task<SourceSummary> Run(FetchedSources input)
    {
        ResumeProbe.Note(nameof(SummarizeSources));
        return Task.FromResult(
            new SourceSummary(
                string.Create(CultureInfo.InvariantCulture, $"{input.Pages} pages on {input.Topic}")
            )
        );
    }
}
