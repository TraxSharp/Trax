using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Tests.Integration.Fakes.Trains;

public sealed class ScoreFindings : Junction<CheckedFindings, Scored>
{
    public override Task<Scored> Run(CheckedFindings input)
    {
        CheckpointProbe.Note(nameof(ScoreFindings));
        return Task.FromResult(new Scored(input.Topic, input.Source, input.Pages / 2));
    }
}
