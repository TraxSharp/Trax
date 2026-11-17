using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Core.Decisions;
using Trax.Core.Exceptions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Data.Decisions;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.Decisions;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.EffectProvider;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Tests.Data.InMemory.Integration.IntegrationTests;

/// <summary>Reads the run's decisions through a context of its own, as another process would.</summary>
public class PeekDecisions(IDataContextProviderFactory factory) : EffectJunction<string, string>
{
    public static List<(long MetadataId, TrainState State, string? Track)> Seen { get; } = [];

    public override async Task<string> Run(string input)
    {
        Seen.Clear();
        var id = Metadata!.TrainMetadataId;
        var context = await factory
            .CreateDbContextAsync(CancellationToken.None)
            .ConfigureAwait(false);
        await using var contextDisposal = context.ConfigureAwait(false);

        var state = await context
            .Metadatas.AsNoTracking()
            .Where(m => m.Id == id)
            .Select(m => m.TrainState)
            .SingleAsync(cancellationToken: CancellationToken.None)
            .ConfigureAwait(false);

        foreach (
            var decision in await context
                .RecordedDecisions.AsNoTracking()
                .Where(d => d.MetadataId == id)
                .ToListAsync(cancellationToken: CancellationToken.None)
                .ConfigureAwait(false)
        )
            Seen.Add((decision.MetadataId, state, decision.Tracks().SingleOrDefault()));

        return input;
    }
}
