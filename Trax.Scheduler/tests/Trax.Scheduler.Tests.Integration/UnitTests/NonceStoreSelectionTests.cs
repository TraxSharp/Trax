using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.RequestSigning;
using Trax.Scheduler.Trains.JobRunner;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

/// <summary>
/// Which <see cref="INonceStore"/> <c>AddTraxJobRunner</c> gives a signing runner: the database by
/// default, memory only when the host says so, and never memory by accident. Enforces
/// <c>docs/adr/0009-a-runner-shares-its-accepted-nonces-through-the-database.md</c>.
/// </summary>
[Property("adr", "docs/adr/0009-a-runner-shares-its-accepted-nonces-through-the-database.md")]
[TestFixture]
public class NonceStoreSelectionTests
{
    private static readonly byte[] Key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();

    private static ServiceProvider Runner(
        Action<TraxJobRunnerOptions> configure,
        bool inMemoryData = false,
        Action<IServiceCollection>? extra = null
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (inMemoryData)
            services.AddTrax(trax =>
                trax.AddEffects(effects => effects.UseInMemory())
                    .AddMediator(typeof(AssemblyMarker).Assembly, typeof(JobRunnerTrain).Assembly)
            );
        extra?.Invoke(services);
        services.AddTraxJobRunner(configure);
        return services.BuildServiceProvider();
    }

    [Test]
    public void SigningKey_WithoutARelationalProvider_RefusesToStart()
    {
        using var provider = Runner(o => o.SigningKey = Key, inMemoryData: true);

        var act = () => provider.GetRequiredService<RunnerRequestVerifier>();

        act.Should()
            .Throw<InvalidOperationException>(
                "a signing runner does not fall back to per-process nonces on its own (see docs/adr/0009-a-runner-shares-its-accepted-nonces-through-the-database.md)"
            )
            .WithMessage("*UseInMemoryNonceStore()*");
    }

    [Test]
    public async Task SigningKey_WithUseInMemoryNonceStore_RefusesARepeatWithinTheProcess()
    {
        using var provider = Runner(o =>
        {
            o.SigningKey = Key;
            o.UseInMemoryNonceStore();
        });
        var verifier = provider.GetRequiredService<RunnerRequestVerifier>();
        var body = "{}"u8.ToArray();
        var signature = RunnerRequestSignature.Create(Key, RunnerRequestPurpose.Run, body);

        provider.GetRequiredService<INonceStore>().Should().BeOfType<InMemoryNonceStore>();
        (await verifier.VerifyAsync(RunnerRequestPurpose.Run, body, signature, true))
            .Should()
            .Be(RunnerRequestVerdict.Accepted);
        (await verifier.VerifyAsync(RunnerRequestPurpose.Run, body, signature, true))
            .Should()
            .Be(RunnerRequestVerdict.Replayed);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task HostRegisteredStore_IsUsed_WhicheverOrderItIsRegisteredIn(bool before)
    {
        var store = new RecordingStore();
        var services = new ServiceCollection();
        services.AddLogging();
        if (before)
            services.AddSingleton<INonceStore>(store);
        services.AddTraxJobRunner(o => o.SigningKey = Key);
        if (!before)
            services.AddSingleton<INonceStore>(store);
        using var provider = services.BuildServiceProvider();
        var body = "{}"u8.ToArray();

        await provider
            .GetRequiredService<RunnerRequestVerifier>()
            .VerifyAsync(
                RunnerRequestPurpose.Run,
                body,
                RunnerRequestSignature.Create(Key, RunnerRequestPurpose.Run, body),
                requireFresh: true
            );

        store.Calls.Should().Be(1);
    }

    [Test]
    public void NoSigningKey_NeedsNoStore()
    {
        using var provider = Runner(o => o.AllowUnsignedRequests(), inMemoryData: true);

        provider
            .Invoking(p => p.GetRequiredService<RunnerRequestVerifier>())
            .Should()
            .NotThrow("a runner without a key has no nonces to keep");
    }

    [Test]
    public void DirectConstruction_SigningKeyWithoutAStore_Throws()
    {
        var act = () =>
            new RunnerRequestVerifier(
                new TraxJobRunnerOptions { SigningKey = Key },
                Microsoft.Extensions.Logging.Abstractions.NullLogger<RunnerRequestVerifier>.Instance
            );

        act.Should().Throw<ArgumentException>().WithMessage("*INonceStore*");
    }

    private sealed class RecordingStore : INonceStore
    {
        public int Calls { get; private set; }

        public ValueTask<bool> TryRecordAsync(
            string nonce,
            DateTimeOffset expiresAt,
            DateTimeOffset now,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            return ValueTask.FromResult(true);
        }
    }
}
