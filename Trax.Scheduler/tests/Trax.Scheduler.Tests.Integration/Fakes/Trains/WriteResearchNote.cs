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

/// <summary>A write committed before the checkpoint: one manifest group per topic.</summary>
public sealed class WriteResearchNote(IDataContext context)
    : EffectJunction<ResearchBrief, ResearchBrief>
{
    public override async Task<ResearchBrief> Run(ResearchBrief input)
    {
        await ResearchProbe.Note(input.Topic, nameof(WriteResearchNote)).ConfigureAwait(false);
        await context
            .Track(
                new ManifestGroup
                {
                    Name =
                        ResearchProbe.NotePrefix + input.Topic + "-" + Guid.NewGuid().ToString("N"),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                }
            )
            .ConfigureAwait(false);
        await context.SaveChanges(CancellationToken.None).ConfigureAwait(false);
        return input;
    }
}
