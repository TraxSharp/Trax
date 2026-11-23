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

/// <summary>Reports the vault's route, which no checkpoint keeps because its answer is sensitive.</summary>
public sealed class ReadVaultRoute : Junction<TrackTaken<Vault>, string>
{
    public override Task<string> Run(TrackTaken<Vault> input)
    {
        CheckpointProbe.Note(nameof(ReadVaultRoute));
        return Task.FromResult(input.Track);
    }
}
