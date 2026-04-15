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
