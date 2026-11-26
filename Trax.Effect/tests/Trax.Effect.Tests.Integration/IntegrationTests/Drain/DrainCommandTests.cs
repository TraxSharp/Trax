using System.Globalization;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Core.Extensions;
using Trax.Effect.Configuration.Deploy;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.Drain;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Testing;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.EffectProvider;
using Trax.Effect.Tests.Integration.Fixtures;

namespace Trax.Effect.Tests.Integration.IntegrationTests.Drain;

/// <summary>
/// What a process a deploy starts with <c>--trax-drain</c> or <c>--trax-resume</c> does (Trax.Docs/adr/0065): it sets
/// the drain, waits for what is busy, and leaves the drain for the roll; past its timeout it ends the drain and refuses,
/// naming what is busy, unless forced; cancelled, it leaves the drain to lapse; and it never starts the host.
/// </summary>
[TestFixture]
[NonParallelizable]
[LeavesStuckRuns("Seeds in-progress runs no host is running, to show a drain waiting on them.")]
public class DrainCommandTests : TestSetup
{
    private static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(50);

    [SetUp]
    public async Task ClearDrain()
    {
        var context = (IDataContext)DataContextFactory.Create();
        await using var disposal = context.ConfigureAwait(false);
        await context.Drains.ExecuteDeleteAsync();
    }

    [TearDown]
    public async Task ClearDrainAfter() => await ClearDrain();

    [Test]
    public async Task With_nothing_busy_the_drain_returns_and_stays_for_the_roll()
    {
        await DrainCommand.DrainAsync(
            Scope.ServiceProvider,
            TimeSpan.FromMinutes(10),
            force: false,
            settle: TimeSpan.Zero,
            poll: Fast,
            CancellationToken.None
        );

        var row = await Row();
        row.Should().NotBeNull("the hosts stay drained until the deploy has rolled them");
        row.NotNull().StartedBy.Should().Be(TraxDrain.DeployActor);
        DateTime
            .SpecifyKind(row.NotNull().Until, DateTimeKind.Utc)
            .Should()
            .BeCloseTo(
                (TimeProvider.System.GetUtcNow() + TraxDrain.Margin).UtcDateTime,
                TimeSpan.FromMinutes(1),
                "the drain holds for the roll from when it returned, the margin by default"
            );
    }

    [Test]
    public async Task A_drain_sets_the_hold_from_when_it_returns_keeping_its_start()
    {
        var hold = TimeSpan.FromHours(2);
        var drain = DrainCommand.DrainAsync(
            Scope.ServiceProvider,
            TimeSpan.FromSeconds(30),
            hold,
            force: false,
            settle: TimeSpan.Zero,
            poll: Fast,
            CancellationToken.None
        );
        await UntilRow();
        var set = (await Row()).NotNull();

        await drain;

        var row = (await Row()).NotNull();
        row.StartedAt.Should().Be(set.StartedAt, "it is the same drain, held longer");
        DateTime
            .SpecifyKind(row.Until, DateTimeKind.Utc)
            .Should()
            .BeCloseTo(
                (TimeProvider.System.GetUtcNow() + hold).UtcDateTime,
                TimeSpan.FromMinutes(1),
                "the roll starts when the drain returns, and the hold covers it from then"
            );
    }

    [Test]
    public async Task Forced_past_the_timeout_the_drain_is_held_from_then()
    {
        await Run(TrainState.InProgress);
        var hold = TimeSpan.FromHours(2);

        await DrainCommand.DrainAsync(
            Scope.ServiceProvider,
            TimeSpan.FromMilliseconds(200),
            hold,
            force: true,
            settle: TimeSpan.Zero,
            poll: Fast,
            CancellationToken.None
        );

        DateTime
            .SpecifyKind((await Row()).NotNull().Until, DateTimeKind.Utc)
            .Should()
            .BeCloseTo(
                (TimeProvider.System.GetUtcNow() + hold).UtcDateTime,
                TimeSpan.FromMinutes(1)
            );
    }

    [Test]
    public async Task Work_that_ends_during_the_wait_lets_the_drain_return()
    {
        var run = await Run(TrainState.InProgress);
        var finish = FinishAfter(run.Id, TimeSpan.FromMilliseconds(300));

        await DrainCommand.DrainAsync(
            Scope.ServiceProvider,
            TimeSpan.FromSeconds(30),
            force: false,
            settle: TimeSpan.Zero,
            poll: Fast,
            CancellationToken.None
        );
        await finish;

        (await Row()).Should().NotBeNull();
    }

    [LeavesStuckRuns(
        "Seeds an in-progress run no host is running, standing for a claim in flight."
    )]
    [Test]
    public async Task Work_that_commits_during_the_settle_is_waited_on()
    {
        // A host that read the row just before it was set may still be claiming; its claim commits moments later.
        // Without the settle the drain would read nothing busy before it does and let the roll cut it off.
        var drain = DrainCommand.DrainAsync(
            Scope.ServiceProvider,
            TimeSpan.FromSeconds(30),
            force: false,
            settle: TimeSpan.FromSeconds(1),
            poll: Fast,
            CancellationToken.None
        );
        await UntilRow();
        var run = await Run(TrainState.InProgress);

        // negative-wait: twice the settle, and the drain must still be waiting on the run.
        await Task.Delay(TimeSpan.FromSeconds(2), TimeProvider.System);
        drain.IsCompleted.Should().BeFalse("the run committed during the settle is busy");

        await SetState(run.Id, TrainState.Completed);
        await drain;
    }

    [Test]
    public async Task Past_the_timeout_the_drain_is_ended_and_refused_naming_what_is_busy()
    {
        var run = await Run(TrainState.InProgress);

        var act = async () =>
            await DrainCommand
                .DrainAsync(
                    Scope.ServiceProvider,
                    TimeSpan.FromMilliseconds(200),
                    force: false,
                    settle: TimeSpan.Zero,
                    poll: Fast,
                    CancellationToken.None
                )
                .ConfigureAwait(false);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage(
                $"*Still busy*{TraxDrain.ForceVariable}=true*run {run.Id.ToString(CultureInfo.InvariantCulture)} Drain.Tests.ITrain (InProgress*"
            );
        (await Row())
            .Should()
            .BeNull("a refused deploy ends the drain, so the hosts take work again");
    }

    [Test]
    public async Task Forced_past_the_timeout_the_drain_returns_and_stays()
    {
        await Run(TrainState.InProgress);

        await DrainCommand.DrainAsync(
            Scope.ServiceProvider,
            TimeSpan.FromMilliseconds(200),
            force: true,
            settle: TimeSpan.Zero,
            poll: Fast,
            CancellationToken.None
        );

        (await Row()).Should().NotBeNull("--force rolls with the hosts still drained");
    }

    [Test]
    public async Task An_interrupted_drain_leaves_the_drain_to_lapse()
    {
        await Run(TrainState.InProgress);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        var act = async () =>
            await DrainCommand
                .DrainAsync(
                    Scope.ServiceProvider,
                    TimeSpan.FromMinutes(10),
                    force: false,
                    settle: TimeSpan.Zero,
                    poll: Fast,
                    cancel.Token
                )
                .ConfigureAwait(false);

        await act.Should().ThrowAsync<OperationCanceledException>();
        (await Row())
            .Should()
            .NotBeNull("nothing ends the drain of a deploy that dies; it lapses at its time");
    }

    [Test]
    public async Task A_drain_ended_while_it_waits_refuses_rather_than_report_the_hosts_drained()
    {
        // Resumed from the console mid-wait: the hosts take work again, so nothing busy now says nothing about what
        // the roll would cut off.
        var run = await Run(TrainState.InProgress);
        var drain = DrainCommand.DrainAsync(
            Scope.ServiceProvider,
            TimeSpan.FromSeconds(30),
            force: false,
            settle: TimeSpan.Zero,
            poll: Fast,
            CancellationToken.None
        );
        await UntilRow();

        await DrainCommand.ResumeAsync(Scope.ServiceProvider, CancellationToken.None);
        await SetState(run.Id, TrainState.Completed);

        await FluentActions
            .Awaiting(() => drain)
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*drain was ended or replaced while it waited*");
    }

    [Test]
    public async Task A_drain_replaced_while_it_waits_refuses_and_leaves_the_new_one()
    {
        var run = await Run(TrainState.InProgress);
        var drain = DrainCommand.DrainAsync(
            Scope.ServiceProvider,
            TimeSpan.FromSeconds(30),
            force: false,
            settle: TimeSpan.Zero,
            poll: Fast,
            CancellationToken.None
        );
        await UntilRow();

        var context = (IDataContext)DataContextFactory.Create();
        await using (context.ConfigureAwait(false))
        {
            var now = TimeProvider.System.GetUtcNow();
            await DrainStore.SetAsync(
                context,
                now + TimeSpan.FromMinutes(5),
                now + TimeSpan.FromSeconds(1),
                "operator:bob",
                CancellationToken.None
            );
        }
        await SetState(run.Id, TrainState.Completed);

        await FluentActions
            .Awaiting(() => drain)
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*drain was ended or replaced while it waited*");
        (await Row())
            .NotNull()
            .StartedBy.Should()
            .Be("operator:bob", "the drain in force is someone else's");
    }

    [Test]
    public async Task A_drain_replaced_just_before_the_busy_read_that_finds_nothing_refuses()
    {
        // The replacement and the run's end land between the drain's ownership look and its busy read, so the busy
        // read finds nothing; the drain must still see it is no longer its own.
        var run = await Run(TrainState.InProgress);
        var services = new HookedServices(
            Scope.ServiceProvider,
            BeforeBusyRead,
            async () =>
            {
                await ReplaceWithOperatorDrain().ConfigureAwait(false);
                await SetState(run.Id, TrainState.Completed).ConfigureAwait(false);
            }
        );

        await FluentActions
            .Awaiting(() =>
                DrainCommand.DrainAsync(
                    services,
                    TimeSpan.FromSeconds(30),
                    force: false,
                    settle: TimeSpan.Zero,
                    poll: Fast,
                    CancellationToken.None
                )
            )
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*drain was ended or replaced while it waited*");
        (await Row()).NotNull().StartedBy.Should().Be("operator:bob");
    }

    [Test]
    public async Task A_drain_replaced_just_before_the_timeout_is_not_ended_by_the_refusal()
    {
        await Run(TrainState.InProgress);
        var services = new HookedServices(
            Scope.ServiceProvider,
            BeforeBusyRead,
            ReplaceWithOperatorDrain
        );

        await FluentActions
            .Awaiting(() =>
                DrainCommand.DrainAsync(
                    services,
                    TimeSpan.FromTicks(1),
                    force: false,
                    settle: TimeSpan.Zero,
                    poll: Fast,
                    CancellationToken.None
                )
            )
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*drain was ended or replaced while it waited*");
        (await Row())
            .NotNull()
            .StartedBy.Should()
            .Be("operator:bob", "a refused deploy ends only its own drain");
    }

    [Test]
    public async Task Resume_ends_the_drain()
    {
        await DrainCommand.DrainAsync(
            Scope.ServiceProvider,
            TimeSpan.FromMinutes(1),
            force: false,
            settle: TimeSpan.Zero,
            poll: Fast,
            CancellationToken.None
        );

        await DrainCommand.ResumeAsync(Scope.ServiceProvider, CancellationToken.None);

        (await Row()).Should().BeNull();
    }

    [Test]
    public async Task RunTraxAsync_resumes_without_starting_the_host()
    {
        await DrainCommand.DrainAsync(
            Scope.ServiceProvider,
            TimeSpan.FromMinutes(1),
            force: false,
            settle: TimeSpan.Zero,
            poll: Fast,
            CancellationToken.None
        );
        var host = new UnstartedHost(Scope.ServiceProvider);

        await host.RunTraxAsync([TraxDrain.ResumeArgument]);

        host.Started.Should().BeFalse();
        (await Row()).Should().BeNull();
    }

    [Test]
    public async Task RunTraxAsync_drains_from_the_process_arguments_without_starting_the_host()
    {
        var host = new UnstartedHost(Scope.ServiceProvider);
        var started = TimeProvider.System.GetTimestamp();

        await host.RunTraxAsync(
            args: [],
            processExportPath: null,
            processMigrates: false,
            CancellationToken.None,
            processArgs: ["app.dll", TraxDrain.Argument, "00:01:00"],
            forceVariable: null
        );

        host.Started.Should().BeFalse();
        (await Row()).Should().NotBeNull();
        TimeProvider
            .System.GetElapsedTime(started)
            .Should()
            .BeGreaterThanOrEqualTo(
                DrainCommand.Settle,
                "a deploy's drain waits the settle before it first reads what is busy"
            );
    }

    [Test]
    public async Task RunTraxAsync_refuses_a_bad_force_value_before_draining()
    {
        var host = new UnstartedHost(Scope.ServiceProvider);

        var act = async () =>
            await host.RunTraxAsync(
                    args: [TraxDrain.Argument],
                    processExportPath: null,
                    processMigrates: false,
                    CancellationToken.None,
                    processArgs: null,
                    forceVariable: "yes"
                )
                .ConfigureAwait(false);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*TRAX_DRAIN_FORCE*");
        (await Row()).Should().BeNull();
    }

    [Test]
    public async Task RunTraxAsync_refuses_a_bad_hold_value_before_draining()
    {
        var host = new UnstartedHost(Scope.ServiceProvider);

        var act = async () =>
            await host.RunTraxAsync(
                    args: [TraxDrain.Argument],
                    processExportPath: null,
                    processMigrates: false,
                    CancellationToken.None,
                    processArgs: null,
                    forceVariable: null,
                    holdVariable: "an hour"
                )
                .ConfigureAwait(false);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*TRAX_DRAIN_HOLD*");
        (await Row()).Should().BeNull();
    }

    /// <summary>
    /// Which context the drain opens first for its busy read: after setting the row, reading it back and checking it is
    /// still its own.
    /// </summary>
    private const int BeforeBusyRead = 4;

    private async Task ReplaceWithOperatorDrain()
    {
        var context = (IDataContext)DataContextFactory.Create();
        await using var disposal = context.ConfigureAwait(false);
        var now = TimeProvider.System.GetUtcNow();
        await DrainStore
            .SetAsync(
                context,
                now + TimeSpan.FromMinutes(5),
                now + TimeSpan.FromSeconds(1),
                "operator:bob",
                CancellationToken.None
            )
            .ConfigureAwait(false);
    }

    private async Task UntilRow()
    {
        var deadline = TimeProvider.System.GetUtcNow() + TimeSpan.FromSeconds(30);
        while (
            await Row().ConfigureAwait(false) is null && TimeProvider.System.GetUtcNow() < deadline
        )
            // allowed-delay: waits for the drain under test to set its row, bounded by 30 s.
            await Task.Delay(TimeSpan.FromMilliseconds(10), TimeProvider.System)
                .ConfigureAwait(false);
        (await Row().ConfigureAwait(false)).Should().NotBeNull("the drain sets its row first");
    }

    private async Task SetState(long id, TrainState state)
    {
        var context = (IDataContext)DataContextFactory.Create();
        await using var disposal = context.ConfigureAwait(false);
        await context
            .Metadatas.Where(m => m.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.TrainState, state))
            .ConfigureAwait(false);
    }

    private async Task<Models.Drain.Drain?> Row()
    {
        var context = (IDataContext)DataContextFactory.Create();
        await using var disposal = context.ConfigureAwait(false);
        return await context.Drains.AsNoTracking().FirstOrDefaultAsync().ConfigureAwait(false);
    }

    private async Task FinishAfter(long id, TimeSpan delay)
    {
        // allowed-delay: the work has to end while the drain waits, so it ends after the drain's
        // first look; the drain polls every 50 ms and its timeout is 30 s.
        await Task.Delay(delay, TimeProvider.System).ConfigureAwait(false);
        var context = (IDataContext)DataContextFactory.Create();
        await using var disposal = context.ConfigureAwait(false);
        await context
            .Metadatas.Where(m => m.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.TrainState, TrainState.Completed))
            .ConfigureAwait(false);
    }

    private async Task<Metadata> Run(TrainState state)
    {
        var context = (IDataContext)DataContextFactory.Create();
        await using var disposal = context.ConfigureAwait(false);
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "Drain.Tests.ITrain",
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            },
            TimeProvider.System
        );
        metadata.TrainState = state;
        await context.Track(metadata).ConfigureAwait(false);
        await context.SaveChanges(CancellationToken.None).ConfigureAwait(false);
        return metadata;
    }

    /// <summary>
    /// The test's services, with a data context factory that runs <paramref name="hook"/> once, before it opens its
    /// <paramref name="call"/>th context, to land a change at an exact point in the drain's sequence of reads.
    /// </summary>
    private sealed class HookedServices(IServiceProvider inner, int call, Func<Task> hook)
        : IServiceProvider
    {
        private readonly HookedFactory _factory = new(
            inner.GetRequiredService<IDataContextProviderFactory>(),
            call,
            hook
        );

        public object? GetService(Type serviceType) =>
            serviceType == typeof(IDataContextProviderFactory)
                ? _factory
                : inner.GetService(serviceType);
    }

    private sealed class HookedFactory(IDataContextProviderFactory inner, int call, Func<Task> hook)
        : IDataContextProviderFactory
    {
        private int _calls;

        public IEffectProvider Create() => inner.Create();

        public async Task<IDataContext> CreateDbContextAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) == call)
                await hook().ConfigureAwait(false);
            return await inner.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>A host that records whether anything started it.</summary>
    private sealed class UnstartedHost(IServiceProvider services) : IHost
    {
        public bool Started { get; private set; }

        public IServiceProvider Services { get; } = services;

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            Started = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Dispose() { }
    }
}
