using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Exceptions;
using Trax.Core.Monad;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models;
using Trax.Effect.Services.Checkpoints;
using Trax.Effect.Services.EffectProvider;
using Trax.Effect.Services.EffectProviderFactory;
using Trax.Effect.Tests.Integration.Fakes.Trains;
using Trax.Effect.Tests.Integration.Fixtures;

namespace Trax.Effect.Tests.Integration.IntegrationTests;

/// <summary>
/// What a run's checkpoints write to <c>trax.checkpoint</c>, on each data provider: the declared
/// state, the routes taken before it, the chain's hash and the state's fingerprint, one row per
/// node, through a context of their own so nothing else the run tracks is saved; and what they
/// refuse, each as a permanent failure of the step that stores nothing: a state over the cap, a
/// step whose data context holds uncommitted work, a state reaching a sensitive member, and a
/// checkpoint inside a sensitive track. A completed run deletes its checkpoints.
///
/// <para>Enforces Trax.Docs/adr/0047-a-checkpoint-stores-a-state-the-train-declares-and-a-resume-skips-to-it.md.</para>
/// </summary>
[TestFixture(CheckpointStoreKind.Postgres)]
[TestFixture(CheckpointStoreKind.Sqlite)]
[TestFixture(CheckpointStoreKind.InMemory)]
[NonParallelizable]
[Property("adr", Adr)]
public class CheckpointWriteTests(CheckpointStoreKind store)
{
    private const string Adr =
        "Trax.Docs/adr/0047-a-checkpoint-stores-a-state-the-train-declares-and-a-resume-skips-to-it.md";

    private readonly ScriptedResearchDecider _decider = new();
    private readonly SaveCounter _saves = new();
    private CheckpointHost _host = null!;
    private readonly List<long> _runs = [];

    [OneTimeSetUp]
    public void CreateHost() =>
        _host = CheckpointHost.Create(
            store,
            _decider,
            services =>
            {
                services.AddSingleton<IEffectProviderFactory>(_saves);
                services
                    .AddScopedTraxRoute<IResearchTrain, ResearchTrain>()
                    .AddScopedTraxRoute<IDirtyResearchTrain, DirtyResearchTrain>()
                    .AddScopedTraxRoute<ISecretTrain, SecretTrain>()
                    .AddScopedTraxRoute<IVaultThenCheckpointTrain, VaultThenCheckpointTrain>()
                    .AddScopedTraxRoute<ICheckpointInVaultTrain, CheckpointInVaultTrain>()
                    .AddScopedTraxRoute<ITwoBranchTrain, TwoBranchTrain>()
                    .AddScopedTraxRoute<ICountingResearchTrain, CountingResearchTrain>();
            }
        );

    [OneTimeTearDown]
    public async Task DisposeHost() => await _host.DisposeAsync();

    [SetUp]
    public void Reset()
    {
        CheckpointProbe.Reset();
        _decider.Reset();
        _saves.Saves = 0;
        _runs.Clear();
    }

    [TearDown]
    public async Task DeleteRuns()
    {
        CheckpointProbe.Reset();
        await _host.Delete([.. _runs]);

        await using var context = await _host
            .Services.GetRequiredService<IDataContextProviderFactory>()
            .CreateDbContextAsync(CancellationToken.None);
        var dirty = await context
            .ManifestGroups.Where(g => g.Name.StartsWith(CheckpointWriteNames.DirtyGroup))
            .ToListAsync();
        context.ManifestGroups.RemoveRange(dirty);
        await context.SaveChanges(CancellationToken.None);
    }

    [Test]
    public async Task A_checkpoint_stores_its_state_tracks_hash_and_fingerprint_once_per_node()
    {
        CheckpointProbe.FailIn = nameof(Summarize);

        var run = await Run<IResearchTrain>();

        run.TrainState.Should().Be(TrainState.Failed);
        var rows = await _host.Checkpoints(run.Id);
        rows.Select(r => r.NodeId)
            .Should()
            .Equal(
                ["Checkpoint<CheckedFindings>#0", "Checkpoint<Scored>#0"],
                $"one row per checkpoint the run reached ({Adr})"
            );

        var first = rows[0];
        first.BranchPath.Should().BeNull();
        first.StateType.Should().Be(typeof(CheckedFindings).FullName);
        JsonSerializer
            .Deserialize<CheckedFindings>(first.State)
            .Should()
            .Be(new CheckedFindings("graphs", "papers", 12, ""));
        Routes(first.Tracks)
            .Should()
            .Equal([(typeof(ResearchSource).FullName!, "Papers")], "the route taken before it");
        first
            .ChainHash.Should()
            .Be(
                ChainGraph
                    .From(
                        new ResearchTrain().DeclaredChain(),
                        typeof(ResearchTrain),
                        typeof(string),
                        typeof(string)
                    )
                    .Hash
            );
        first.StateFingerprint.Should().Be(Fingerprint(typeof(CheckedFindings)));
        rows[1].StateFingerprint.Should().Be(Fingerprint(typeof(Scored)));
        rows[1].ChainHash.Should().Be(first.ChainHash);
        JsonSerializer
            .Deserialize<Scored>(rows[1].State)
            .Should()
            .Be(new Scored("graphs", "papers", 6));
    }

    [Test]
    public async Task A_checkpoint_over_the_cap_fails_the_step_as_permanent_and_stores_nothing()
    {
        var options = _host.Services.GetRequiredService<CheckpointOptions>();
        var cap = options.MaxStateBytes;
        options.MaxStateBytes = 1024;

        try
        {
            CheckpointProbe.Padding = new string('x', 2048);

            var run = await Run<IResearchTrain>();

            run.TrainState.Should().Be(TrainState.Failed);
            run.FailureClass.Should().Be(FailureClass.Permanent, $"a retry fails the same ({Adr})");
            run.FailureReason.Should().Contain("the cap is 1024");
            (await _host.Checkpoints(run.Id)).Should().BeEmpty("a placeholder is never stored");
            CheckpointProbe.Ran.Should().NotContain(nameof(ScoreFindings));
        }
        finally
        {
            options.MaxStateBytes = cap;
        }
    }

    [TestCase(DirtyMode.UnsavedChange)]
    [TestCase(DirtyMode.OpenTransaction)]
    public async Task A_checkpoint_over_uncommitted_changes_or_an_open_transaction_fails_and_stores_nothing(
        DirtyMode dirty
    )
    {
        if (dirty == DirtyMode.OpenTransaction && store == CheckpointStoreKind.InMemory)
            Assert.Ignore("The InMemory provider has no transactions to leave open.");

        CheckpointProbe.Dirty = dirty;

        var run = await Run<IDirtyResearchTrain>();

        run.TrainState.Should().Be(TrainState.Failed);
        run.FailureClass.Should().Be(FailureClass.Permanent);
        run.FailureReason.Should().Contain("uncommitted");
        (await _host.Checkpoints(run.Id))
            .Should()
            .BeEmpty($"a resume after it would skip work that never committed ({Adr})");
        CheckpointProbe.Ran.Should().NotContain(nameof(ScoreFindings));
    }

    [Test]
    public async Task A_checkpoint_after_committed_work_is_stored()
    {
        CheckpointProbe.FailIn = nameof(Summarize);

        var run = await Run<IDirtyResearchTrain>();

        run.FailureReason.Should().Contain("Summarize timed out");
        (await _host.Checkpoints(run.Id)).Should().ContainSingle();
    }

    [Test]
    public async Task A_checkpoint_saves_no_other_effect_provider()
    {
        CheckpointProbe.FailIn = nameof(Summarize);
        CheckpointProbe.SaveCount = () => _saves.Saves;

        var run = await Run<IResearchTrain>();

        (await _host.Checkpoints(run.Id)).Should().HaveCount(2);
        _saves.Saves.Should().BeGreaterThan(0, "the counter is one of the run's providers");
        CheckpointProbe
            .SavesAt[nameof(Summarize)]
            .Should()
            .Be(
                CheckpointProbe.SavesAt[nameof(FetchFullTexts)],
                $"no provider saved between the step before the checkpoints and the step after "
                    + $"them: a checkpoint writes through a context of its own ({Adr}, effect/0021)"
            );
    }

    [Test]
    public async Task A_sensitive_state_is_refused_at_startup_and_at_write_after_an_assembly_loads()
    {
        // The write-time half: whatever the startup check covered, the store checks the state type
        // again when the checkpoint is written, and refuses one that reaches a sensitive member.
        var run = await Run<ISecretTrain>();

        run.TrainState.Should().Be(TrainState.Failed);
        run.FailureClass.Should().Be(FailureClass.Permanent);
        run.FailureReason.Should().Contain("[TraxSensitive]");
        (await _host.Checkpoints(run.Id)).Should().BeEmpty($"it is never stored ({Adr})");
        CheckpointProbe.Ran.Should().NotContain(nameof(Reveal));
    }

    [Test]
    public async Task A_sensitive_routing_track_is_never_stored_and_a_checkpoint_inside_it_is_refused()
    {
        CheckpointProbe.FailIn = nameof(Summarize);

        var after = await Run<IVaultThenCheckpointTrain>();

        var row = (await _host.Checkpoints(after.Id)).Should().ContainSingle().Subject;
        Routes(row.Tracks)
            .Should()
            .Equal(
                [(typeof(ResearchSource).FullName!, "Papers")],
                $"the vault's track is withheld, as from junction events ({Adr})"
            );
        row.Tracks.Should().NotContain("Left");

        CheckpointProbe.Reset();
        var inside = await Run<ICheckpointInVaultTrain>();

        inside.TrainState.Should().Be(TrainState.Failed);
        inside.FailureClass.Should().Be(FailureClass.Permanent);
        inside.FailureReason.Should().Contain("[TraxSensitive]").And.NotContain("Left");
        inside.FailureJunction.Should().NotContain("Left", "the failure names no withheld track");
        (await _host.Checkpoints(inside.Id))
            .Should()
            .BeEmpty("its node id names the withheld track, and a resume could not find it");
    }

    [Test]
    public async Task A_completed_run_deletes_its_checkpoints()
    {
        var run = await Run<IResearchTrain>();

        run.TrainState.Should().Be(TrainState.Completed);
        CheckpointProbe.Ran.Should().Contain(nameof(Summarize));
        (await _host.Checkpoints(run.Id))
            .Should()
            .BeEmpty($"nothing may resume a completed run ({Adr})");
    }

    [Test]
    public async Task The_running_train_declares_its_chain_once_even_when_it_stores_a_checkpoint()
    {
        // Found by the full test pass: a run read its chain hash by calling DeclaredChain on itself,
        // which calls Junctions() a second time on the running instance. The hash is read on a fresh
        // instance instead, and only once the run reaches a checkpoint.
        CountingResearchTrain.Calls.Clear();

        var run = await Run<ICountingResearchTrain>();

        run.TrainState.Should().Be(TrainState.Completed, run.FailureReason);
        CountingResearchTrain
            .Calls.GroupBy(c => c.Instance)
            .Should()
            .OnlyContain(
                instance => instance.Count() == 1,
                $"each instance's Junctions() runs once: the run's to run, another to read ({Adr})"
            );
        CountingResearchTrain.Calls.Should().ContainSingle(c => !c.Declaring);
    }

    [Test]
    public async Task Two_branches_writing_checkpoints_at_once_store_both()
    {
        CheckpointProbe.BothBranches = new CountdownEvent(2);
        CheckpointProbe.FailIn = nameof(Join);

        var run = await Run<ITwoBranchTrain>();

        run.FailureReason.Should().Contain("Join timed out");
        var rows = await _host.Checkpoints(run.Id);
        rows.Select(r => (r.NodeId, r.BranchPath))
            .Should()
            .BeEquivalentTo([
                ("Parallel#0/a/Checkpoint<BranchA>#0", "Parallel#0/a"),
                ("Parallel#0/b/Checkpoint<BranchB>#0", "Parallel#0/b"),
            ]);
    }

    private async Task<Models.Metadata.Metadata> Run<TTrain>()
        where TTrain : class, Effect.Services.ServiceTrain.IServiceTrain<string, string>
    {
        var (run, _) = await _host.Run<TTrain>("graphs");
        _runs.Add(run.Id);
        return run;
    }

    private static List<(string Key, string Track)> Routes(string tracks) =>
        JsonNode
            .Parse(tracks)!
            .AsArray()
            .Select(t => ((string)t!["key"]!, (string)t["track"]!))
            .ToList();

    // CheckpointState is internal to Trax.Core; the fingerprint it computes is the stored one.
    private static string Fingerprint(Type state) =>
        (string)
            typeof(ChainRecorder)
                .Assembly.GetType("Trax.Core.Monad.CheckpointState")!
                .GetMethod("Fingerprint")!
                .Invoke(null, [state])!;

    /// <summary>An effect provider that counts every save the effect runner asks of it.</summary>
    private sealed class SaveCounter : IEffectProviderFactory, IEffectProvider
    {
        public int Saves { get; set; }

        public IEffectProvider Create() => this;

        public Task SaveChanges(CancellationToken cancellationToken)
        {
            Saves++;
            return Task.CompletedTask;
        }

        public Task Track(IModel model) => Task.CompletedTask;

        public Task Update(IModel model) => Task.CompletedTask;

        public void Dispose() { }
    }
}
