using System.Security.Cryptography;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Data.Decisions;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Tests.Data.InMemory.Integration.IntegrationTests;

/// <summary>
/// The hash of each decision's state is keyed when the host supplies a key, in code or in
/// configuration, and without one the journal records no hash for a state that can hold a value
/// marked <c>[TraxSensitive]</c>, so such an answer is never replayed.
///
/// <para>Enforces docs/adr/0020-a-recorded-answer-replays-only-while-it-is-fresh.md.</para>
/// </summary>
[Property("adr", "docs/adr/0020-a-recorded-answer-replays-only-while-it-is-fresh.md")]
public class StateHashKeyTests
{
    private const string Adr = "docs/adr/0020-a-recorded-answer-replays-only-while-it-is-fresh.md";

    private static readonly byte[] Key = RandomNumberGenerator.GetBytes(32);

    private static ServiceProvider Provider(
        Action<DecisionRecordingOptions>? configure = null,
        IConfiguration? configuration = null,
        ILogger<DecisionJournal>? logger = null,
        IDecider? decider = null
    )
    {
        var services = new ServiceCollection()
            .AddSingleton(decider ?? new ScriptedDecider().Choose(Desk.Counter))
            .AddScopedTraxRoute<IRouteClaim, RouteClaim>()
            .AddScopedTraxRoute<IRouteVisit, RouteVisit>()
            .AddScopedTraxRoute<IRouteKennel, RouteKennel>()
            .AddScopedTraxRoute<Systematic.Tests.IRouteFiling, Systematic.Tests.RouteFiling>();

        if (configuration is not null)
            services.AddSingleton(configuration);
        if (logger is not null)
            services.AddSingleton(logger);

        return services
            .AddTrax(trax =>
                trax.AddEffects(effects =>
                    effects.UseInMemory().AddDecisionRecording(configure ?? (_ => { }))
                )
            )
            .BuildServiceProvider();
    }

    private static async Task<long> Run<TTrain, TIn>(
        IServiceProvider provider,
        TIn input,
        long? replayDecisionsOf = null
    )
        where TTrain : class, IServiceTrain<TIn, string>
    {
        var scope = provider.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var train =
            (ServiceTrain<TIn, string>)(object)scope.ServiceProvider.GetRequiredService<TTrain>();
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(TTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = input!,
                ReplayDecisionsOf = replayDecisionsOf,
            }
        );
        await train.Run(input, metadata, CancellationToken.None).ConfigureAwait(false);
        return train.Metadata!.Id;
    }

    private static async Task<string?> StateHash(IServiceProvider provider, long metadataId)
    {
        var scope = provider.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        return await scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .RecordedDecisions.AsNoTracking()
            .Where(d => d.MetadataId == metadataId)
            .Select(d => d.StateHash)
            .SingleAsync()
            .ConfigureAwait(false);
    }

    [Test]
    public async Task Without_a_key_the_state_hash_is_unkeyed()
    {
        var provider = Provider();
        await using var providerDisposal = provider.ConfigureAwait(false);

        var run = await Run<IRouteVisit, Visit>(provider, new Visit("v1"));

        (await StateHash(provider, run)).Should().MatchRegex("^s1:[0-9a-f]{64}$");
    }

    [Test]
    public async Task A_key_given_in_code_keys_the_state_hash()
    {
        var unkeyed = Provider();
        await using var unkeyedDisposal = unkeyed.ConfigureAwait(false);
        var keyed = Provider(o => o.HashStatesWith(Key));
        await using var keyedDisposal = keyed.ConfigureAwait(false);

        var plain = await StateHash(
            unkeyed,
            await Run<IRouteVisit, Visit>(unkeyed, new Visit("v2"))
        );
        var hashed = await StateHash(keyed, await Run<IRouteVisit, Visit>(keyed, new Visit("v2")));

        hashed.Should().MatchRegex("^k1:[0-9a-f]{64}$", $"the host gave a key. See {Adr}.");
        hashed![3..].Should().NotBe(plain![3..]);
    }

    [Test]
    public async Task A_key_in_configuration_keys_the_state_hash()
    {
        var configuration = new OneValueConfiguration(
            DecisionRecordingOptions.StateHashKeyConfigurationKey,
            Convert.ToBase64String(Key)
        );
        var provider = Provider(configuration: configuration);
        await using var providerDisposal = provider.ConfigureAwait(false);

        var run = await Run<IRouteVisit, Visit>(provider, new Visit("v3"));

        (await StateHash(provider, run)).Should().MatchRegex("^k1:[0-9a-f]{64}$");
    }

    [Test]
    public void A_key_shorter_than_32_bytes_is_refused()
    {
        var options = (DecisionRecordingOptions)
            Activator.CreateInstance(typeof(DecisionRecordingOptions), nonPublic: true)!;

        var act = () => options.HashStatesWith(new byte[16]);

        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public async Task Without_a_key_a_sensitive_state_is_recorded_without_its_hash_and_never_replayed()
    {
        var logger = new CapturingJournalLogger();
        var provider = Provider(logger: logger);
        await using var providerDisposal = provider.ConfigureAwait(false);

        var first = await Run<IRouteClaim, Claim>(provider, new Claim("c1", "078-05-1120"));
        await Run<IRouteClaim, Claim>(provider, new Claim("c2", "219-09-9999"));

        (await StateHash(provider, first))
            .Should()
            .BeNull($"an unkeyed hash of a sensitive state is not stored. See {Adr}.");
        logger
            .Messages.Where(m => m.Contains("HashStatesWith", StringComparison.Ordinal))
            .Should()
            .ContainSingle("the warning is logged once per state type");

        var requeue = await Run<IRouteClaim, Claim>(
            provider,
            new Claim("c1", "078-05-1120"),
            replayDecisionsOf: first
        );
        var scope = provider.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
            (
                await scope
                    .ServiceProvider.GetRequiredService<IDataContext>()
                    .RecordedDecisions.AsNoTracking()
                    .SingleAsync(d => d.MetadataId == requeue)
            )
                .Replayed.Should()
                .BeFalse(
                    $"an answer recorded without its state's hash is asked afresh. See {Adr}."
                );
        (await StateHash(provider, requeue)).Should().BeNull();
    }

    [Test]
    public async Task With_a_key_a_sensitive_state_is_recorded_with_its_keyed_hash()
    {
        var provider = Provider(o => o.HashStatesWith(Key));
        await using var providerDisposal = provider.ConfigureAwait(false);

        var run = await Run<IRouteClaim, Claim>(provider, new Claim("c3", "078-05-1120"));

        (await StateHash(provider, run)).Should().MatchRegex("^k1:[0-9a-f]{64}$");
    }

    [Test]
    public void A_sensitive_member_is_found_however_deep_it_is_held()
    {
        Trax.Effect.Utils.TraxRedaction.ReachesSensitiveMember(typeof(Claim)).Should().BeTrue();
        Trax.Effect.Utils.TraxRedaction.ReachesSensitiveMember(typeof(Batch)).Should().BeTrue();
        Trax.Effect.Utils.TraxRedaction.ReachesSensitiveMember(typeof(List<Claim>))
            .Should()
            .BeTrue();
        Trax.Effect.Utils.TraxRedaction.ReachesSensitiveMember(typeof(Visit)).Should().BeFalse();
    }

    [Test]
    public void A_sensitive_subtype_held_as_its_base_or_as_object_is_found()
    {
        foreach (
            var holder in new[]
            {
                typeof(HoldsObject),
                typeof(HoldsInterface),
                typeof(HoldsBase),
                typeof(HoldsAbstract),
                typeof(HoldsListOfObject),
                typeof(HoldsWrapped),
                typeof(Kennel),
            }
        )
            Trax.Effect.Utils.TraxRedaction.ReachesSensitiveMember(holder)
                .Should()
                .BeTrue($"{holder.Name} can hold a value with a marked member. See {Adr}.");

        Trax.Effect.Utils.TraxRedaction.ReachesSensitiveMember(typeof(HoldsNote))
            .Should()
            .BeFalse("nothing that can be held as a Note carries a mark");
    }

    [Test]
    public void A_state_type_is_looked_into_whatever_its_namespace_is()
    {
        Trax.Effect.Utils.TraxRedaction.ReachesSensitiveMember(typeof(Systematic.Tests.Filing))
            .Should()
            .BeTrue($"only framework assemblies are skipped, not namespaces. See {Adr}.");
    }

    [Test]
    public async Task Without_a_key_a_state_holding_a_sensitive_subtype_is_recorded_without_its_hash()
    {
        var provider = Provider();
        await using var providerDisposal = provider.ConfigureAwait(false);

        var run = await Run<IRouteKennel, Kennel>(
            provider,
            new Kennel("p1", new SecretPet { Name = "Rex", TaxId = "078-05-1120" })
        );

        (await StateHash(provider, run))
            .Should()
            .BeNull($"the state's Pet can be a pet with a marked member. See {Adr}.");
    }

    [Test]
    public async Task Without_a_key_a_sensitive_state_outside_a_framework_namespace_is_recorded_without_its_hash()
    {
        var provider = Provider();
        await using var providerDisposal = provider.ConfigureAwait(false);

        var run = await Run<Systematic.Tests.IRouteFiling, Systematic.Tests.Filing>(
            provider,
            new Systematic.Tests.Filing("f1", "078-05-1120")
        );

        (await StateHash(provider, run)).Should().BeNull();
    }
}
