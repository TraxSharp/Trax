using System.Reflection;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Models.DeadLetter.DTOs;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Services.EffectProvider;
using Trax.Mediator.Services.TrainRegistry;
using Trax.Scheduler.Services.CancellationRegistry;
using Trax.Scheduler.Services.TraxScheduler;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// <see cref="IDataContext"/> is an interface, and a host may hand the scheduler one that is not
/// an Entity Framework <see cref="DbContext"/> (a decorator, say). The scheduler then takes the
/// per-row path it takes on a store without set-based updates, rather than casting.
/// </summary>
[TestFixture]
public class NonEntityFrameworkDataContextTests
{
    [Test]
    public async Task Acknowledging_all_dead_letters_works_through_a_decorated_data_context()
    {
        await using var fx = SchedulerE2EFixture.CreateInMemory(_ => { });
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(SchedulerTestTrain),
                IsEnabled = true,
                ScheduleType = ScheduleType.Interval,
                IntervalSeconds = 60,
                Properties = new SchedulerTestInput { Value = "decorated" },
            }
        );
        await fx.DataContext.Track(manifest);
        await fx.DataContext.SaveChanges(CancellationToken.None);
        await fx.DataContext.Track(
            DeadLetter.Create(
                new CreateDeadLetter
                {
                    Manifest = manifest,
                    Reason = "seeded",
                    RetryCount = 3,
                }
            )
        );
        await fx.DataContext.SaveChanges(CancellationToken.None);
        fx.DataContext.Reset();

        var scheduler = new TraxScheduler(
            new DecoratingFactory(fx.Services.GetRequiredService<IDataContextProviderFactory>()),
            fx.Services.GetRequiredService<ITrainRegistry>(),
            fx.Services.GetRequiredService<ICancellationRegistry>(),
            NullLogger<TraxScheduler>.Instance
        );

        var result = await scheduler.AcknowledgeAllDeadLettersAsync("handled");

        result.Count.Should().Be(1);
        (await fx.DataContext.DeadLetters.AsNoTracking().SingleAsync())
            .Status.Should()
            .Be(DeadLetterStatus.Acknowledged);
    }

    /// <summary>Hands out each context wrapped, so it is an <see cref="IDataContext"/> only.</summary>
    private sealed class DecoratingFactory(IDataContextProviderFactory inner)
        : IDataContextProviderFactory
    {
        public IEffectProvider Create() => Decorator.Wrap((IDataContext)inner.Create());

        public async Task<IDataContext> CreateDbContextAsync(CancellationToken cancellationToken) =>
            Decorator.Wrap(await inner.CreateDbContextAsync(cancellationToken));
    }

    /// <summary>Forwards every <see cref="IDataContext"/> member to the context it wraps.</summary>
    public class Decorator : DispatchProxy
    {
        private IDataContext _inner = null!;

        public static IDataContext Wrap(IDataContext inner)
        {
            var proxy = Create<IDataContext, Decorator>();
            ((Decorator)(object)proxy)._inner = inner;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            try
            {
                return targetMethod!.Invoke(_inner, args);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                throw ex.InnerException;
            }
        }
    }
}
