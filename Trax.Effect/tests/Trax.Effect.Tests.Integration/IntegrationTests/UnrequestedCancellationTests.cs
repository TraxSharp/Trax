using System.Net;
using System.Net.Sockets;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Trax.Core.Exceptions;
using Trax.Core.Functional;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.FailureClassifier;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Tests.Integration.IntegrationTests;

/// <summary>
/// A run ends <c>Cancelled</c> only when something asked it to stop: its own token, or the
/// persisted cancel flag the dashboard and the scheduler's job timeout set. Any other
/// <see cref="OperationCanceledException"/>, such as an <see cref="HttpClient"/> timeout, is a
/// failed run classified <see cref="FailureClass.Transient"/>, so a manifest retries it.
///
/// <para>Enforces Trax.Docs/adr/0020-a-failure-is-classified-where-it-happens-and-carried.md.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0020-a-failure-is-classified-where-it-happens-and-carried.md")]
public class UnrequestedCancellationTests
{
    private ServiceProvider _provider = null!;
    private static readonly ConfigurableClassifier Classifier = new();

    [OneTimeSetUp]
    public void BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory().AddJunctionProgress())
        );
        services
            .AddSingleton<IFailureClassifier>(Classifier)
            .AddScopedTraxRoute<ITokenCancelledTrain, TokenCancelledTrain>()
            .AddScopedTraxRoute<IUnrequestedTrain, UnrequestedTrain>()
            .AddScopedTraxRoute<IHttpTimeoutTrain, HttpTimeoutTrain>()
            .AddScopedTraxRoute<IFlagCancelledTrain, FlagCancelledTrain>();
        _provider = services.BuildServiceProvider();
    }

    [OneTimeTearDown]
    public async Task DisposeProvider() => await _provider.DisposeAsync();

    [SetUp]
    public void ResetClassifier() => Classifier.Result = null;

    [Test]
    public async Task A_cancellation_the_trains_token_requested_is_recorded_cancelled()
    {
        using var scope = _provider.CreateScope();
        var train = (TokenCancelledTrain)
            scope.ServiceProvider.GetRequiredService<ITokenCancelledTrain>();
        using var cts = new CancellationTokenSource();
        TokenCancelledTrain.Source = cts;

        var act = async () => await train.Run(Unit.Default, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        var row = await PersistedRow(scope, train.Metadata!.Id);
        row.TrainState.Should().Be(TrainState.Cancelled);
        row.FailureClass.Should().Be(FailureClass.Unclassified);
        train.Hooks.Should().Equal("OnCancelled");
    }

    [Test]
    public async Task A_cancellation_the_trains_token_did_not_request_is_a_transient_failure()
    {
        using var scope = _provider.CreateScope();
        var train = (UnrequestedTrain)scope.ServiceProvider.GetRequiredService<IUnrequestedTrain>();

        var act = async () => await train.Run(Unit.Default);
        await act.Should().ThrowAsync<OperationCanceledException>();

        var row = await PersistedRow(scope, train.Metadata!.Id);
        row.TrainState.Should()
            .Be(
                TrainState.Failed,
                "nothing asked this run to stop, so it failed, and a manifest counts only Failed "
                    + "toward a retry"
            );
        row.FailureClass.Should().Be(FailureClass.Transient);
        train.Hooks.Should().Equal("OnFailed");
    }

    [Test]
    public async Task An_HttpClient_timeout_is_a_transient_failure()
    {
        // A listener that accepts the connection into its backlog and never answers, so the
        // request can end only by the client's own timeout.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        using var scope = _provider.CreateScope();
        var train = (HttpTimeoutTrain)scope.ServiceProvider.GetRequiredService<IHttpTimeoutTrain>();
        HttpTimeoutTrain.Target = new Uri(
            $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/"
        );

        var act = async () => await train.Run(Unit.Default);
        await act.Should().ThrowAsync<TaskCanceledException>();

        var row = await PersistedRow(scope, train.Metadata!.Id);
        row.TrainState.Should().Be(TrainState.Failed);
        row.FailureClass.Should().Be(FailureClass.Transient);
        row.FailureException.Should().Be(nameof(TaskCanceledException));
    }

    [Test]
    public async Task A_classifier_answer_for_an_unrequested_cancellation_wins()
    {
        Classifier.Result = FailureClass.Permanent;
        using var scope = _provider.CreateScope();
        var train = (UnrequestedTrain)scope.ServiceProvider.GetRequiredService<IUnrequestedTrain>();

        var act = async () => await train.Run(Unit.Default);
        await act.Should().ThrowAsync<OperationCanceledException>();

        var row = await PersistedRow(scope, train.Metadata!.Id);
        row.TrainState.Should().Be(TrainState.Failed);
        row.FailureClass.Should()
            .Be(
                FailureClass.Permanent,
                "Transient is Trax's answer only when the consumer's classifier has none"
            );
    }

    [Test]
    public async Task A_cancellation_requested_through_the_persisted_flag_is_recorded_cancelled()
    {
        using var scope = _provider.CreateScope();
        var train = (FlagCancelledTrain)
            scope.ServiceProvider.GetRequiredService<IFlagCancelledTrain>();

        var act = async () => await train.Run(Unit.Default);
        await act.Should().ThrowAsync<OperationCanceledException>();

        var row = await PersistedRow(scope, train.Metadata!.Id);
        row.TrainState.Should()
            .Be(
                TrainState.Cancelled,
                "the dashboard and the scheduler's job timeout ask a run to stop through the "
                    + "persisted flag, which the next junction boundary turns into a cancellation"
            );
        row.FailureClass.Should().Be(FailureClass.Unclassified);
    }

    private static async Task<Metadata> PersistedRow(IServiceScope scope, long id)
    {
        var factory = scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        using var context = (IDataContext)factory.Create();

        return await context.Metadatas.AsNoTracking().SingleAsync(m => m.Id == id);
    }

    // ── probes ──────────────────────────────────────────────────────

    public class ConfigurableClassifier : IFailureClassifier
    {
        public FailureClass? Result { get; set; }

        public FailureClass? Classify(Exception exception) => Result;
    }

    public abstract class HookRecordingTrain : ServiceTrain<Unit, Unit>
    {
        public List<string> Hooks { get; } = [];

        protected override Task OnFailed(
            Metadata metadata,
            Exception exception,
            CancellationToken ct
        )
        {
            Hooks.Add("OnFailed");
            return Task.CompletedTask;
        }

        protected override Task OnCancelled(Metadata metadata, CancellationToken ct)
        {
            Hooks.Add("OnCancelled");
            return Task.CompletedTask;
        }
    }

    public interface ITokenCancelledTrain : IServiceTrain<Unit, Unit>;

    public class TokenCancelledTrain : HookRecordingTrain, ITokenCancelledTrain
    {
        public static CancellationTokenSource? Source { get; set; }

        protected override Task<Either<Exception, Unit>> Junctions() =>
            Chain<CancelsTheCallerJunction>().Resolve();

        /// <summary>Cancels the caller's source, then observes the train's token.</summary>
        public class CancelsTheCallerJunction : EffectJunction<Unit, Unit>
        {
            public override Task<Unit> Run(Unit input)
            {
                Source!.Cancel();
                CancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(input);
            }
        }
    }

    public interface IUnrequestedTrain : IServiceTrain<Unit, Unit>;

    public class UnrequestedTrain : HookRecordingTrain, IUnrequestedTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() =>
            Chain<ThrowsUnrequestedJunction>().Resolve();

        public class ThrowsUnrequestedJunction : EffectJunction<Unit, Unit>
        {
            public override Task<Unit> Run(Unit input) =>
                throw new OperationCanceledException("a downstream call gave up on its own");
        }
    }

    public interface IHttpTimeoutTrain : IServiceTrain<Unit, Unit>;

    public class HttpTimeoutTrain : HookRecordingTrain, IHttpTimeoutTrain
    {
        public static Uri? Target { get; set; }

        protected override Task<Either<Exception, Unit>> Junctions() =>
            Chain<CallsUpstreamJunction>().Resolve();

        public class CallsUpstreamJunction : EffectJunction<Unit, Unit>
        {
            public override async Task<Unit> Run(Unit input)
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(200) };
                await client.GetAsync(Target, CancellationToken);
                return input;
            }
        }
    }

    public interface IFlagCancelledTrain : IServiceTrain<Unit, Unit>;

    public class FlagCancelledTrain : HookRecordingTrain, IFlagCancelledTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() =>
            Chain<RequestsCancelJunction>().Chain<NeverReachedJunction>().Resolve();

        /// <summary>Sets the persisted flag the way the dashboard's cancel button does.</summary>
        public class RequestsCancelJunction(IDataContextProviderFactory factory)
            : EffectJunction<Unit, Unit>
        {
            public override async Task<Unit> Run(Unit input)
            {
                var id = Metadata!.TrainMetadataId;
                using var context = (IDataContext)factory.Create();
                var row = await context.Metadatas.SingleAsync(m => m.Id == id);
                row.CancellationRequested = true;
                await context.SaveChanges(CancellationToken.None);
                return input;
            }
        }

        public class NeverReachedJunction : EffectJunction<Unit, Unit>
        {
            public override Task<Unit> Run(Unit input) => Task.FromResult(input);
        }
    }
}
