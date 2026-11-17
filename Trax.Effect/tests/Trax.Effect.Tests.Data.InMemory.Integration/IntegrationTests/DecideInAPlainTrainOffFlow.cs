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

/// <summary>
/// Runs a plain train on a thread that does not carry the run's flow, under the run's external id,
/// reporting to the host's journal.
/// </summary>
public class DecideInAPlainTrainOffFlow(
    IDecider decider,
    IDecisionObserver observer,
    IDecisionReplay replay
) : Junction<Order, Order>
{
    public static string ExternalId { get; set; } = "";

    public static bool Decided { get; private set; }

    public override async Task<Order> Run(Order input)
    {
        Decided = false;
        Task<Order> plain;

        using (ExecutionContext.SuppressFlow())
            plain = Task.Run(() =>
                new PlainRush(decider, observer, replay) { ExternalId = ExternalId }.Run(
                    input,
                    CancellationToken.None
                )
            );

        var output = await plain.ConfigureAwait(false);
        Decided = true;
        return output;
    }
}
