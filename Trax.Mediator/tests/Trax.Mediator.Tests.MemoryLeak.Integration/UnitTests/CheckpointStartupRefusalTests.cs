using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Extensions;
using Trax.Effect.Services.Checkpoints;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Configuration;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.ChainVerification;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.UnitTests;

/// <summary>
/// What the host refuses about a train's checkpoints before it serves traffic: a state that reaches
/// a member marked <c>[TraxSensitive]</c>, and a checkpoint inside a track whose routing key is
/// sensitive. And the size a checkpoint may be, which follows the cap a requeue's input has.
///
/// <para>Enforces Trax.Docs/adr/0047-a-checkpoint-stores-a-state-the-train-declares-and-a-resume-skips-to-it.md.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0047-a-checkpoint-stores-a-state-the-train-declares-and-a-resume-skips-to-it.md"
)]
[TestFixture]
public class CheckpointStartupRefusalTests
{
    private const string Adr =
        "Trax.Docs/adr/0047-a-checkpoint-stores-a-state-the-train-declares-and-a-resume-skips-to-it.md";

    [Test]
    public async Task A_state_reaching_a_sensitive_member_is_refused_at_startup()
    {
        var failure = await Start<ISecretTrain, SecretTrain>();
        failure.Should().NotBeNull();

        failure!.Message.Should().Contain("Checkpoint<Secret>").And.Contain("[TraxSensitive]", Adr);
    }

    [Test]
    public async Task A_checkpoint_inside_a_sensitive_routing_track_is_refused_at_startup()
    {
        var failure = await Start<IWithheldTrackTrain, WithheldTrackTrain>(services =>
            services.AddSingleton<IDecider>(new ScriptedDecider())
        );
        failure.Should().NotBeNull();

        failure!
            .Message.Should()
            .Contain("inside a track whose routing key is marked [TraxSensitive]", Adr);
    }

    [Test]
    public async Task A_plain_checkpoint_starts()
    {
        (await Start<IPlainTrain, PlainTrain>()).Should().BeNull(Adr);
    }

    [Test]
    public void The_checkpoint_cap_is_the_cap_a_requeues_stored_input_has()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects)
                .AddMediator(m =>
                    m.ScanAssemblies(typeof(PlainTrain).Assembly).WithMaxInputJsonBytes(1000)
                )
        );

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<CheckpointOptions>().MaxStateBytes.Should().Be(4000, Adr);
    }

    private static async Task<Exception?> Start<TService, TTrain>(
        Action<IServiceCollection>? configure = null
    )
        where TService : class
        where TTrain : class, TService
    {
        var services = new ServiceCollection();
        services.AddSingleton<IServiceCollection>(services);
        services.AddLogging();
        services.AddTrax(trax => trax.AddEffects(effects => effects));
        services.AddScoped<TService, TTrain>();
        configure?.Invoke(services);

        await using var provider = services.BuildServiceProvider();

        var discovery = Substitute.For<ITrainDiscoveryService>();
        discovery.DiscoverTrains().Returns([Registration<TService, TTrain>()]);

        var validator = new TrainChainStartupValidator(
            discovery,
            provider.GetRequiredService<IServiceScopeFactory>(),
            new MediatorConfiguration()
        );

        try
        {
            await validator.StartingAsync(CancellationToken.None);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static TrainRegistration Registration<TService, TTrain>() =>
        new()
        {
            ServiceType = typeof(TService),
            ImplementationType = typeof(TTrain),
            InputType = typeof(CheckpointProbe),
            OutputType = typeof(bool),
            Lifetime = ServiceLifetime.Scoped,
            ServiceTypeName = typeof(TService).Name,
            ImplementationTypeName = typeof(TTrain).Name,
            InputTypeName = nameof(CheckpointProbe),
            OutputTypeName = nameof(Boolean),
            HasAllowAnonymousAttribute = true,
            RequiredPolicies = [],
            RequiredRoles = [],
            IsQuery = false,
            IsMutation = false,
            IsRemote = false,
            IsBroadcastEnabled = false,
            GraphQLOperations = 0,
        };

    /// <summary>An input no other train in this assembly takes, so scanning finds these alone for it.</summary>
    public sealed record CheckpointProbe(string Value);

    public sealed record Secret([property: TraxSensitive] string Token);

    public sealed record Plain(string Name);

    [TraxSensitive]
    [Asks("Is it secret?")]
    public sealed class IsSecret;

    public sealed class MakeSecret : Junction<CheckpointProbe, Secret>
    {
        public override Task<Secret> Run(CheckpointProbe input) =>
            Task.FromResult(new Secret(input.Value));
    }

    public sealed class MakePlain : Junction<CheckpointProbe, Plain>
    {
        public override Task<Plain> Run(CheckpointProbe input) =>
            Task.FromResult(new Plain(input.Value));
    }

    public sealed class Done<T> : Junction<T, bool>
    {
        public override Task<bool> Run(T input) => Task.FromResult(true);
    }

    public interface ISecretTrain : IServiceTrain<CheckpointProbe, bool>;

    public sealed class SecretTrain : ServiceTrain<CheckpointProbe, bool>, ISecretTrain
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<MakeSecret>().Checkpoint<Secret>().Chain<Done<Secret>>().Resolve();
    }

    public interface IPlainTrain : IServiceTrain<CheckpointProbe, bool>;

    public sealed class PlainTrain : ServiceTrain<CheckpointProbe, bool>, IPlainTrain
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<MakePlain>().Checkpoint<Plain>().Chain<Done<Plain>>().Resolve();
    }

    public interface IWithheldTrackTrain : IServiceTrain<CheckpointProbe, bool>;

    public sealed class WithheldTrackTrain
        : ServiceTrain<CheckpointProbe, bool>,
            IWithheldTrackTrain
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<MakePlain>()
                .Gate<Plain, IsSecret>(g =>
                    g.Yes(y => y.Checkpoint<Plain>().Chain<Done<Plain>>())
                        .No(n => n.Chain<Done<Plain>>())
                )
                .Resolve();
    }
}
