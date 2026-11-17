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

/// <summary>Leaves the step's Trax data context with work it has not committed.</summary>
public sealed class MakeDirty(IDataContext context) : Junction<CheckedFindings, CheckedFindings>
{
    public override async Task<CheckedFindings> Run(CheckedFindings input)
    {
        CheckpointProbe.Note(nameof(MakeDirty));

        switch (CheckpointProbe.Dirty)
        {
            case DirtyMode.UnsavedChange:
                await context
                    .Track(
                        new ManifestGroup
                        {
                            Name = $"{CheckpointWriteNames.DirtyGroup}{Guid.NewGuid():N}",
                        }
                    )
                    .ConfigureAwait(false);
                break;
            case DirtyMode.OpenTransaction
                when context is DbContext { Database: var database } && database.IsSqlite():
                // Deferred, so it holds no lock until it writes: an immediate one would hold
                // SQLite's write lock and make the run's own terminal write wait it out.
                await database.OpenConnectionAsync().ConfigureAwait(false);
                database.UseTransaction(
                    ((SqliteConnection)database.GetDbConnection()).BeginTransaction(deferred: true)
                );
                break;
            case DirtyMode.OpenTransaction:
                await context.BeginTransaction().ConfigureAwait(false);
                break;
        }

        return input;
    }
}
