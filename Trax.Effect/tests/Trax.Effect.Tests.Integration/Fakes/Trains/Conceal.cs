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

public sealed class Conceal : Junction<Findings, SecretFindings>
{
    public override Task<SecretFindings> Run(Findings input)
    {
        CheckpointProbe.Note(nameof(Conceal));
        return Task.FromResult(new SecretFindings(input.Topic, "s3cr3t"));
    }
}
