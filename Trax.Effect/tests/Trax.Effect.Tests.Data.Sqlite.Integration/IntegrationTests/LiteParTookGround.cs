using System.Collections.Concurrent;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Decisions;
using Trax.Core.Extensions;
using Trax.Core.Functional;
using Trax.Core.Monad;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.JunctionEvents;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Sqlite.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Models.JunctionRun;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Tests.Data.Sqlite.Integration.IntegrationTests;

public class LiteParTookGround : EffectJunction<LiteParOrder, Unit>
{
    public override Task<Unit> Run(LiteParOrder input)
    {
        LiteParTook.Note(Metadata.NotNull().TrainMetadataId, nameof(LiteParLane.Ground));
        return Task.FromResult(Unit.Default);
    }
}
