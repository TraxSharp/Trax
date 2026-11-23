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

public sealed class OpenVaultLeft : Junction<Findings, Findings>
{
    public override Task<Findings> Run(Findings input)
    {
        CheckpointProbe.Note(nameof(OpenVaultLeft));
        return Task.FromResult(input);
    }
}
