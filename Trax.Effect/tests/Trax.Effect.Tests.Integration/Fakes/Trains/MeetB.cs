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

/// <inheritdoc cref="MeetA" />
public sealed class MeetB : Junction<BranchB, BranchB>
{
    public override Task<BranchB> Run(BranchB input)
    {
        CheckpointProbe.BothBranches?.Signal();
        CheckpointProbe.BothBranches?.Wait(TimeSpan.FromSeconds(30), CancellationToken.None);
        return Task.FromResult(input);
    }
}
