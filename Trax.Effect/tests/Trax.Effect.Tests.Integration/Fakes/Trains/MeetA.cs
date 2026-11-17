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

/// <summary>Holds its branch until the other one is here too, so both checkpoint at once.</summary>
public sealed class MeetA : Junction<BranchA, BranchA>
{
    public override Task<BranchA> Run(BranchA input)
    {
        CheckpointProbe.BothBranches?.Signal();
        CheckpointProbe.BothBranches?.Wait(TimeSpan.FromSeconds(30));
        return Task.FromResult(input);
    }
}
