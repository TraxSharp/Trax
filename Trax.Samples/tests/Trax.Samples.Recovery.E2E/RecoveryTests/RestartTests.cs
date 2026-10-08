using Npgsql;
using Trax.Samples.Recovery.Auth;
using Trax.Samples.Recovery.Corpus;
using Trax.Samples.Recovery.E2E.Factories;
using Trax.Samples.Recovery.E2E.Utilities;
using Trax.Samples.Recovery.Index;
using Trax.Samples.Recovery.Machines;
using Trax.Samples.Shared.Testing;

namespace Trax.Samples.Recovery.E2E.RecoveryTests;

/// <summary>
/// The topic map survives its host dying in the middle of a build. A machine-invoked run has no manifest
/// retry, so "recovers" means: the host that starts next fails the orphaned run on its startup recovery,
/// the outcome reconciler moves the draft from <c>Building</c> to <c>BuildFailed</c>, and a rebuild from
/// there works. The machine's draft and the seeded data are in Postgres, so both are still there.
/// </summary>
/// <remarks>
/// Each host is a process of its own on a database of its own: disposing an in-process host would stop
/// its runs and record them <c>Cancelled</c>, which is a clean shutdown, not a crash.
/// </remarks>
[TestFixture]
public class RestartTests
{
    [Test]
    public async Task AHostKilledMidBuild_FailsTheRunOnTheNextStart_AndTheDraftIsRebuiltFromBuildFailed()
    {
        Guid draftId;
        await using (var first = await RecoveryHostProcess.StartAsync(TimeSpan.FromSeconds(2)))
        {
            var draft = new TopicMapDraft(new GraphQLClient(first.Http), DemoKeys.Operator);
            draftId = draft.Id;
            await draft.CreateAsync();
            (await draft.BuildAsync(CorpusFixture.Fields, 2016, 2025))
                .State.Should()
                .Be(nameof(TopicMapState.Building));

            (
                await Polling.WaitUntilAsync(
                    async () => await RunStateAsync(draftId) == "in_progress",
                    TopicMapDraft.Patience,
                    TimeSpan.FromMilliseconds(100)
                )
            )
                .Should()
                .BeTrue(
                    $"the build should be running on the first host. It wrote:\n{first.Output}"
                );

            await first.KillAsync();
        }

        (await RunStateAsync(draftId))
            .Should()
            .Be("in_progress", "a killed host records nothing: its run is orphaned");
        (await DraftStateAsync(draftId)).Should().Be(nameof(TopicMapState.Building));

        await using var second = await RecoveryHostProcess.StartAsync(
            TimeSpan.FromMilliseconds(200)
        );
        var resumed = new TopicMapDraft(new GraphQLClient(second.Http), DemoKeys.Operator, draftId);

        var failed = await resumed.WaitForStateAsync(nameof(TopicMapState.BuildFailed));
        (await RunStateAsync(draftId)).Should().Be("failed", "the restarted host reaped it");
        failed.Context["fields"]!.AsArray().Should().HaveCount(CorpusFixture.Fields.Count);

        // The seeded data is still there.
        (await CountAsync("topic_map.works"))
            .Should()
            .Be(CorpusFixture.Works.Count);
        (await CountAsync("topic_map.source_records")).Should().Be(IndexFixture.Records.Count);

        // Rebuild from where the crash left the draft: a new run, on the host that is up.
        (await resumed.AdvanceAsync(nameof(TopicMapTrigger.Rebuild)))
            .State.Should()
            .Be(nameof(TopicMapState.Building));
        var built = await resumed.WaitForStateAsync(nameof(TopicMapState.Built));
        built.Context["topicPairs"]!.GetValue<int>().Should().Be(32);
    }

    // The state of the latest run the draft's Building state queued, as Postgres spells it.
    private static async Task<string?> RunStateAsync(Guid draftId) =>
        await ScalarAsync<string>(
            """
            SELECT train_state::text FROM trax.metadata
            WHERE invoking_instance_id = @id ORDER BY id DESC LIMIT 1
            """,
            draftId
        );

    private static async Task<string?> DraftStateAsync(Guid draftId) =>
        await ScalarAsync<string>(
            "SELECT state FROM trax.snapshot_draft WHERE id = @id AND machine = 'topic-map'",
            draftId
        );

    private static async Task<long> CountAsync(string table) =>
        await ScalarAsync<long>($"SELECT count(*) FROM {table}", null);

    private static async Task<T?> ScalarAsync<T>(string sql, Guid? id)
    {
        await using var connection = new NpgsqlConnection(RecoveryHostProcess.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        if (id is { } value)
            command.Parameters.AddWithValue("id", value);
        return await command.ExecuteScalarAsync() is T result ? result : default;
    }
}
