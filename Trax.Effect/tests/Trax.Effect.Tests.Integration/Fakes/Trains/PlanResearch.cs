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

public sealed class PlanResearch : Junction<string, Brief>
{
    public override Task<Brief> Run(string input)
    {
        CheckpointProbe.Note(nameof(PlanResearch));
        return Task.FromResult(new Brief(input));
    }
}
