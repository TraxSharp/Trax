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

public sealed class Summarize : Junction<Scored, string>
{
    public override Task<string> Run(Scored input)
    {
        CheckpointProbe.Note(nameof(Summarize));
        return Task.FromResult($"{input.Topic} from {input.Source}, scored {input.Score}");
    }
}
