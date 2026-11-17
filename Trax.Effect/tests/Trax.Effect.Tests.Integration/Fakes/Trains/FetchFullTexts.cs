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

public sealed class FetchFullTexts : Junction<Findings, CheckedFindings>
{
    public override Task<CheckedFindings> Run(Findings input)
    {
        CheckpointProbe.Note(nameof(FetchFullTexts));
        return Task.FromResult(
            new CheckedFindings(input.Topic, input.Source, 12, CheckpointProbe.Padding)
        );
    }
}
