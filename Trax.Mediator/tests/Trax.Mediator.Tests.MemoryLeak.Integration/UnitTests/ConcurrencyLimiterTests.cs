using FluentAssertions;
using Trax.Mediator.Configuration;
using Trax.Mediator.Services.ConcurrencyLimiter;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.UnitTests;

[TestFixture]
public class ConcurrencyLimiterTests
{
    private const string TrainA = "Namespace.ITrainA";
    private const string TrainB = "Namespace.ITrainB";

    #region No Limits Configured

    [Test]
    public async Task AcquireAsync_NoLimitsConfigured_ReturnsImmediately()
    {
        var limiter = CreateLimiter();

        var permit = await limiter.AcquireAsync(TrainA, CancellationToken.None);

        permit.Should().NotBeNull();
        permit.Dispose();
    }

    #endregion

    #region Per-Train Limits

    [Test]
    public async Task AcquireAsync_PerTrainLimit_BlocksAtLimit()
    {
        var limiter = CreateLimiter(overrides: new() { [TrainA] = 2 });

        var permit1 = await limiter.AcquireAsync(TrainA, CancellationToken.None);
        var permit2 = await limiter.AcquireAsync(TrainA, CancellationToken.None);

        // Third acquire should block
        var acquireTask = limiter.AcquireAsync(TrainA, CancellationToken.None);
        var completed = await Task.WhenAny(acquireTask, Task.Delay(200));

        completed.Should().NotBe(acquireTask, "third acquire should block when limit is 2");

        permit1.Dispose();
        permit2.Dispose();

        // Now it should complete
        var permit3 = await acquireTask;
        permit3.Dispose();
    }

    [Test]
    public async Task AcquireAsync_PerTrainLimit_ReleasesOnDispose()
    {
        var limiter = CreateLimiter(overrides: new() { [TrainA] = 1 });

        var permit1 = await limiter.AcquireAsync(TrainA, CancellationToken.None);

        // Second acquire blocks
        var acquireTask = limiter.AcquireAsync(TrainA, CancellationToken.None);
        var blocked = await Task.WhenAny(acquireTask, Task.Delay(100));
        blocked.Should().NotBe(acquireTask);

        // Release the first permit
        permit1.Dispose();

        // Now second should complete quickly
        var permit2 = await Task.WhenAny(acquireTask, Task.Delay(1000));
        permit2.Should().Be(acquireTask, "acquire should complete after dispose");
        acquireTask.Result.Dispose();
    }

    [Test]
    public async Task AcquireAsync_DifferentTrains_IndependentLimits()
    {
        var limiter = CreateLimiter(overrides: new() { [TrainA] = 1, [TrainB] = 1 });

        var permitA = await limiter.AcquireAsync(TrainA, CancellationToken.None);
        var permitB = await limiter.AcquireAsync(TrainB, CancellationToken.None);

        // Both acquired — they don't interfere
        permitA.Should().NotBeNull();
        permitB.Should().NotBeNull();

        // TrainA is full, TrainB is full — each blocks independently
        var acquireA = limiter.AcquireAsync(TrainA, CancellationToken.None);
        var acquireB = limiter.AcquireAsync(TrainB, CancellationToken.None);

        var blockedA = await Task.WhenAny(acquireA, Task.Delay(100));
        blockedA.Should().NotBe(acquireA);

        var blockedB = await Task.WhenAny(acquireB, Task.Delay(100));
        blockedB.Should().NotBe(acquireB);

        // Release A — only A unblocks
        permitA.Dispose();
        var completedA = await Task.WhenAny(acquireA, Task.Delay(1000));
        completedA.Should().Be(acquireA);
        acquireA.Result.Dispose();

        // B still blocked
        var stillBlockedB = await Task.WhenAny(acquireB, Task.Delay(100));
        stillBlockedB.Should().NotBe(acquireB);

        permitB.Dispose();
        var completedB = await Task.WhenAny(acquireB, Task.Delay(1000));
        completedB.Should().Be(acquireB);
        acquireB.Result.Dispose();
    }

    #endregion

    #region Global Limits

    [Test]
    public async Task AcquireAsync_GlobalLimit_BlocksAcrossTrains()
    {
        var limiter = CreateLimiter(globalLimit: 2);

        var permit1 = await limiter.AcquireAsync(TrainA, CancellationToken.None);
        var permit2 = await limiter.AcquireAsync(TrainB, CancellationToken.None);

        // Global limit of 2 reached — third from any train should block
        var acquireTask = limiter.AcquireAsync(TrainA, CancellationToken.None);
        var completed = await Task.WhenAny(acquireTask, Task.Delay(200));

        completed.Should().NotBe(acquireTask, "global limit should block across trains");

        permit1.Dispose();

        // Now should complete
        var permit3 = await Task.WhenAny(acquireTask, Task.Delay(1000));
        permit3.Should().Be(acquireTask);
        acquireTask.Result.Dispose();
        permit2.Dispose();
    }

    [Test]
    public async Task AcquireAsync_PerTrainAndGlobal_BothEnforced()
    {
        // Per-train limit of 5 but global limit of 2 — global is the bottleneck
        var limiter = CreateLimiter(globalLimit: 2, overrides: new() { [TrainA] = 5 });

        var permit1 = await limiter.AcquireAsync(TrainA, CancellationToken.None);
        var permit2 = await limiter.AcquireAsync(TrainA, CancellationToken.None);

        // Per-train has 3 slots left but global is full
        var acquireTask = limiter.AcquireAsync(TrainA, CancellationToken.None);
        var completed = await Task.WhenAny(acquireTask, Task.Delay(200));

        completed.Should().NotBe(acquireTask, "global limit should be the bottleneck");

        permit1.Dispose();
        permit2.Dispose();
        var permit3 = await acquireTask;
        permit3.Dispose();
    }

    #endregion

    #region Cancellation

    [Test]
    public async Task AcquireAsync_CancellationToken_ThrowsWhenCancelled()
    {
        var limiter = CreateLimiter(overrides: new() { [TrainA] = 1 });

        var permit = await limiter.AcquireAsync(TrainA, CancellationToken.None);

        using var cts = new CancellationTokenSource(100);
        var act = () => limiter.AcquireAsync(TrainA, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();

        permit.Dispose();
    }

    [Test]
    public async Task AcquireAsync_CancelledWhileWaitingForGlobal_ReleasesPerTrainPermit()
    {
        // Per-train limit 10, global limit 1 — acquire fills global, next blocks on global
        var limiter = CreateLimiter(globalLimit: 1, overrides: new() { [TrainA] = 10 });

        var permit = await limiter.AcquireAsync(TrainA, CancellationToken.None);

        using var cts = new CancellationTokenSource(100);

        var act = () => limiter.AcquireAsync(TrainA, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        // Per-train permit should have been released on cancellation
        // Release the global holder and verify we can acquire again
        permit.Dispose();

        var permit2 = await limiter.AcquireAsync(TrainA, CancellationToken.None);
        permit2.Should().NotBeNull();
        permit2.Dispose();
    }

    #endregion

    #region Builder Override Precedence

    [Test]
    public async Task AcquireAsync_BuilderOverride_TakesPrecedenceOverAttribute()
    {
        // Attribute says 1 (via discovery), builder says 3
        var registration = new TrainRegistration
        {
            ServiceType = typeof(IFakeConcurrencyTrain),
            ImplementationType = typeof(FakeConcurrencyTrain),
            InputType = typeof(string),
            OutputType = typeof(string),
            Lifetime = Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient,
            ServiceTypeName = "IFakeConcurrencyTrain",
            ImplementationTypeName = "FakeConcurrencyTrain",
            InputTypeName = "String",
            OutputTypeName = "String",
            RequiredPolicies = [],
            RequiredRoles = [],
            IsQuery = false,
            IsMutation = false,
            IsBroadcastEnabled = false,
            IsRemote = false,
            GraphQLOperations = Effect.Attributes.GraphQLOperation.Run,
            MaxConcurrentRun = 1,
        };

        var discovery = new StubDiscoveryService([registration]);
        var config = new MediatorConfiguration();
        config.ConcurrencyOverrides[typeof(IFakeConcurrencyTrain).FullName!] = 3;

        var limiter = new ConcurrencyLimiter(config, discovery, new NullPrincipalProvider());
        var trainName = typeof(IFakeConcurrencyTrain).FullName!;

        // Should allow 3 concurrent (builder override), not 1 (attribute)
        var permit1 = await limiter.AcquireAsync(trainName, CancellationToken.None);
        var permit2 = await limiter.AcquireAsync(trainName, CancellationToken.None);
        var permit3 = await limiter.AcquireAsync(trainName, CancellationToken.None);

        // All three acquired — attribute limit of 1 was overridden
        permit1.Should().NotBeNull();
        permit2.Should().NotBeNull();
        permit3.Should().NotBeNull();

        // Fourth should block
        var acquireTask = limiter.AcquireAsync(trainName, CancellationToken.None);
        var completed = await Task.WhenAny(acquireTask, Task.Delay(200));
        completed.Should().NotBe(acquireTask);

        permit1.Dispose();
        permit2.Dispose();
        permit3.Dispose();
        var permit4 = await acquireTask;
        permit4.Dispose();
    }

    #endregion

    #region Permit Disposal

    [Test]
    public async Task Permit_Dispose_Idempotent()
    {
        var limiter = CreateLimiter(overrides: new() { [TrainA] = 1 });

        var permit = await limiter.AcquireAsync(TrainA, CancellationToken.None);

        // Dispose twice — should not throw or double-release
        permit.Dispose();
        var act = () => permit.Dispose();
        act.Should().NotThrow();

        // Verify only one slot was released (can acquire exactly once more)
        var permit2 = await limiter.AcquireAsync(TrainA, CancellationToken.None);
        var acquireTask = limiter.AcquireAsync(TrainA, CancellationToken.None);
        var completed = await Task.WhenAny(acquireTask, Task.Delay(200));
        completed.Should().NotBe(acquireTask, "double dispose should not release extra slots");

        permit2.Dispose();
        var permit3 = await acquireTask;
        permit3.Dispose();
    }

    #endregion

    #region Helpers

    private static ConcurrencyLimiter CreateLimiter(
        int? globalLimit = null,
        Dictionary<string, int>? overrides = null
    )
    {
        var config = new MediatorConfiguration { GlobalMaxConcurrentRun = globalLimit };

        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
                config.ConcurrencyOverrides[key] = value;
        }

        var discovery = new StubDiscoveryService([]);
        return new ConcurrencyLimiter(config, discovery, new NullPrincipalProvider());
    }

    private sealed class NullPrincipalProvider
        : Mediator.Services.Principal.ICurrentPrincipalProvider
    {
        public string? GetCurrentPrincipalId() => null;
    }

    private interface IFakeConcurrencyTrain;

    private class FakeConcurrencyTrain : IFakeConcurrencyTrain;

    private class StubDiscoveryService(IReadOnlyList<TrainRegistration> registrations)
        : ITrainDiscoveryService
    {
        public IReadOnlyList<TrainRegistration> DiscoverTrains() => registrations;
    }

    #endregion
}
