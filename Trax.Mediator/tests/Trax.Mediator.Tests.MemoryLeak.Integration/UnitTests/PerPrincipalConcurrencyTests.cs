using FluentAssertions;
using Trax.Mediator.Configuration;
using Trax.Mediator.Services.ConcurrencyLimiter;
using Trax.Mediator.Services.Principal;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.UnitTests;

[TestFixture]
public class PerPrincipalConcurrencyTests
{
    private interface IFakeTrain;

    private sealed class StubPrincipal : ICurrentPrincipalProvider
    {
        public string? CurrentId { get; set; }

        public string? GetCurrentPrincipalId() => CurrentId;
    }

    private sealed class ThrowingPrincipal : ICurrentPrincipalProvider
    {
        public bool Throw { get; set; } = true;

        public string? GetCurrentPrincipalId() =>
            Throw ? throw new InvalidOperationException("no principal outside a request") : null;
    }

    private sealed class StubDiscovery : ITrainDiscoveryService
    {
        public IReadOnlyList<TrainRegistration> DiscoverTrains() =>
            Array.Empty<TrainRegistration>();
    }

    [Test]
    public async Task CapDisabled_WhenConfigNull_NoPrincipalBucketing()
    {
        var config = new MediatorConfiguration();
        var principal = new StubPrincipal { CurrentId = "alice" };
        var limiter = new ConcurrencyLimiter(config, new StubDiscovery(), principal);
        var train = typeof(IFakeTrain).FullName!;

        // Many concurrent acquires for the same principal: all proceed because
        // PerPrincipalMaxConcurrentRun is null.
        var permits = new List<IDisposable>();
        for (var i = 0; i < 20; i++)
            permits.Add(await limiter.AcquireAsync(train, CancellationToken.None));

        permits.Should().HaveCount(20);
        foreach (var p in permits)
            p.Dispose();
    }

    [Test]
    public async Task AThrowingPrincipalProvider_HandsBackThePerTrainSlot()
    {
        var train = typeof(IFakeTrain).FullName!;
        var config = new MediatorConfiguration { PerPrincipalMaxConcurrentRun = 1 };
        config.ConcurrencyOverrides[train] = 1;
        var principal = new ThrowingPrincipal();
        var limiter = new ConcurrencyLimiter(config, new StubDiscovery(), principal);

        var failing = () => limiter.AcquireAsync(train, CancellationToken.None);
        await failing.Should().ThrowAsync<InvalidOperationException>();

        principal.Throw = false;
        var next = () =>
            limiter.AcquireAsync(train, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        (
            await next.Should()
                .NotThrowAsync(
                    "the failed acquire took the train's only slot, and a slot it never returns "
                        + "blocks that train for the life of the process"
                )
        ).Subject.Dispose();
    }

    [Test]
    public async Task ManyPrincipals_RunOneAfterAnother_LeaveNoSemaphoreBehind()
    {
        var config = new MediatorConfiguration { PerPrincipalMaxConcurrentRun = 1 };
        var principal = new StubPrincipal();
        var limiter = new ConcurrencyLimiter(config, new StubDiscovery(), principal);
        var train = typeof(IFakeTrain).FullName!;

        for (var i = 0; i < 100; i++)
        {
            principal.CurrentId = $"principal-{i}";
            using var permit = await limiter.AcquireAsync(train, CancellationToken.None);
            limiter.PerPrincipalEntryCount.Should().Be(1);
        }

        limiter
            .PerPrincipalEntryCount.Should()
            .Be(
                0,
                "a principal with nothing running needs no semaphore, and keeping one per id ever "
                    + "seen grows with every distinct principal for the life of the process"
            );
    }

    [Test]
    public async Task APrincipalsSemaphore_OutlivesACancelledWaiter_UntilTheLastRunReleases()
    {
        var config = new MediatorConfiguration { PerPrincipalMaxConcurrentRun = 1 };
        var principal = new StubPrincipal { CurrentId = "alice" };
        var limiter = new ConcurrencyLimiter(config, new StubDiscovery(), principal);
        var train = typeof(IFakeTrain).FullName!;

        var held = await limiter.AcquireAsync(train, CancellationToken.None);

        using var cts = new CancellationTokenSource();
        var waiting = limiter.AcquireAsync(train, cts.Token);
        await cts.CancelAsync();
        await FluentActions
            .Awaiting(() => waiting)
            .Should()
            .ThrowAsync<OperationCanceledException>();

        limiter
            .PerPrincipalEntryCount.Should()
            .Be(1, "the run still holding alice's slot keeps her semaphore");

        held.Dispose();
        limiter.PerPrincipalEntryCount.Should().Be(0);

        // A fresh semaphore starts with the full limit.
        using var next = await limiter
            .AcquireAsync(train, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task ACancelledGlobalWait_HandsBackThePrincipalSlotItHeld()
    {
        var config = new MediatorConfiguration
        {
            PerPrincipalMaxConcurrentRun = 1,
            GlobalMaxConcurrentRun = 1,
        };
        var principal = new StubPrincipal { CurrentId = "bob" };
        var limiter = new ConcurrencyLimiter(config, new StubDiscovery(), principal);
        var train = typeof(IFakeTrain).FullName!;

        // Bob holds the only global slot.
        var bob = await limiter.AcquireAsync(train, CancellationToken.None);

        // Alice's first run takes her one slot and waits for the global one; her second waits
        // for her slot behind it.
        principal.CurrentId = "alice";
        using var cancelFirst = new CancellationTokenSource();
        var first = limiter.AcquireAsync(train, cancelFirst.Token);
        var second = limiter.AcquireAsync(train, CancellationToken.None);

        await cancelFirst.CancelAsync();
        await FluentActions.Awaiting(() => first).Should().ThrowAsync<OperationCanceledException>();

        bob.Dispose();

        using var secondPermit = await second.WaitAsync(TimeSpan.FromSeconds(5));
        limiter
            .PerPrincipalEntryCount.Should()
            .Be(1, "alice's second run holds her slot, which the cancelled run gave back");
    }

    [Test]
    public async Task SamePrincipal_OverCap_BlocksUntilRelease()
    {
        var config = new MediatorConfiguration { PerPrincipalMaxConcurrentRun = 2 };
        var principal = new StubPrincipal { CurrentId = "alice" };
        var limiter = new ConcurrencyLimiter(config, new StubDiscovery(), principal);
        var train = typeof(IFakeTrain).FullName!;

        var permit1 = await limiter.AcquireAsync(train, CancellationToken.None);
        var permit2 = await limiter.AcquireAsync(train, CancellationToken.None);

        // Third acquire must block.
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        Func<Task> act = async () => await limiter.AcquireAsync(train, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        // After releasing one, a new acquire succeeds immediately.
        permit1.Dispose();
        var permit3 = await limiter.AcquireAsync(train, CancellationToken.None);
        permit3.Should().NotBeNull();

        permit2.Dispose();
        permit3.Dispose();
    }

    [Test]
    public async Task DifferentPrincipals_EachHaveOwnBudget()
    {
        var config = new MediatorConfiguration { PerPrincipalMaxConcurrentRun = 1 };
        var principal = new StubPrincipal { CurrentId = "alice" };
        var limiter = new ConcurrencyLimiter(config, new StubDiscovery(), principal);
        var train = typeof(IFakeTrain).FullName!;

        var permitAlice = await limiter.AcquireAsync(train, CancellationToken.None);

        // Switch "current principal" to Bob before acquiring. Bob gets his own slot.
        principal.CurrentId = "bob";
        var permitBob = await limiter.AcquireAsync(train, CancellationToken.None);
        permitBob.Should().NotBeNull();

        permitAlice.Dispose();
        permitBob.Dispose();
    }

    [Test]
    public async Task AnonymousCaller_NotSubjectToCap()
    {
        var config = new MediatorConfiguration { PerPrincipalMaxConcurrentRun = 1 };
        var principal = new StubPrincipal { CurrentId = null };
        var limiter = new ConcurrencyLimiter(config, new StubDiscovery(), principal);
        var train = typeof(IFakeTrain).FullName!;

        var permits = new List<IDisposable>();
        for (var i = 0; i < 5; i++)
            permits.Add(await limiter.AcquireAsync(train, CancellationToken.None));

        permits.Should().HaveCount(5);
        foreach (var p in permits)
            p.Dispose();
    }

    [Test]
    public async Task DisposingPermit_ReleasesPrincipalSlot()
    {
        var config = new MediatorConfiguration { PerPrincipalMaxConcurrentRun = 1 };
        var principal = new StubPrincipal { CurrentId = "alice" };
        var limiter = new ConcurrencyLimiter(config, new StubDiscovery(), principal);
        var train = typeof(IFakeTrain).FullName!;

        var permit1 = await limiter.AcquireAsync(train, CancellationToken.None);
        permit1.Dispose();

        // After release, a second acquire proceeds immediately.
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var permit2 = await limiter.AcquireAsync(train, cts.Token);

        permit2.Should().NotBeNull();
        permit2.Dispose();
    }
}
