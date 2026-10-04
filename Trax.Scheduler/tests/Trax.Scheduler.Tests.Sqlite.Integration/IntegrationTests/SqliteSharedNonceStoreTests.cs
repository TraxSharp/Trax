using System.Text;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Sqlite.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.RequestSigning;
using Trax.Scheduler.Trains.JobRunner;

namespace Trax.Scheduler.Tests.Sqlite.Integration.IntegrationTests;

/// <summary>
/// The database nonce store on Sqlite: the same model and data context calls as on Postgres, with
/// Sqlite's own reading of a primary-key conflict. Enforces <c>docs/adr/0009-a-runner-shares-its-accepted-nonces-through-the-database.md</c>.
/// </summary>
[Property("adr", "docs/adr/0009-a-runner-shares-its-accepted-nonces-through-the-database.md")]
[TestFixture]
public class SqliteSharedNonceStoreTests
{
    private static readonly byte[] Key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
    private string _dbPath = null!;

    [SetUp]
    public void CreateDatabasePath() =>
        _dbPath = Path.Combine(Path.GetTempPath(), $"trax_nonce_test_{Guid.NewGuid():N}.db");

    [TearDown]
    public void DeleteDatabase()
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
            if (File.Exists(_dbPath + suffix))
                File.Delete(_dbPath + suffix);
    }

    private ServiceProvider RunnerInstance() =>
        new ServiceCollection()
            .AddLogging()
            .AddTrax(trax =>
                trax.AddEffects(effects => effects.UseSqlite($"Data Source={_dbPath}"))
                    .AddMediator(typeof(AssemblyMarker).Assembly, typeof(JobRunnerTrain).Assembly)
            )
            .AddTraxJobRunner(runner => runner.SigningKey = Key)
            .BuildServiceProvider();

    [Test]
    public async Task ARequestAcceptedByOneInstance_IsRefusedByTheOther()
    {
        await using var first = RunnerInstance();
        await using var second = RunnerInstance();
        var body = Encoding.UTF8.GetBytes("""{"trainName":"x"}""");
        var signature = RunnerRequestSignature.Create(Key, RunnerRequestPurpose.Run, body);

        (
            await first
                .GetRequiredService<RunnerRequestVerifier>()
                .VerifyAsync(RunnerRequestPurpose.Run, body, signature, requireFresh: true)
        )
            .Should()
            .Be(RunnerRequestVerdict.Accepted);
        (
            await second
                .GetRequiredService<RunnerRequestVerifier>()
                .VerifyAsync(RunnerRequestPurpose.Run, body, signature, requireFresh: true)
        )
            .Should()
            .Be(
                RunnerRequestVerdict.Replayed,
                "instances on one database share the nonces they accept (see docs/adr/0009-a-runner-shares-its-accepted-nonces-through-the-database.md)"
            );
    }

    [Test]
    public async Task AnExpiredRecord_IsTakenOver()
    {
        await using var instance = RunnerInstance();
        var store = instance.GetRequiredService<INonceStore>();
        var now = DateTimeOffset.UtcNow;

        (await store.TryRecordAsync("a", now.AddMinutes(-1), now, CancellationToken.None))
            .Should()
            .BeTrue();
        (await store.TryRecordAsync("a", now.AddMinutes(5), now, CancellationToken.None))
            .Should()
            .BeTrue();
        (await store.TryRecordAsync("a", now.AddMinutes(5), now, CancellationToken.None))
            .Should()
            .BeFalse();
    }

    [Test]
    public async Task TwoInstancesRacingOneNonce_RecordItOnce()
    {
        await using var first = RunnerInstance();
        await using var second = RunnerInstance();
        var stores = new[]
        {
            first.GetRequiredService<INonceStore>(),
            second.GetRequiredService<INonceStore>(),
        };
        var now = DateTimeOffset.UtcNow;

        var recorded = await Task.WhenAll(
            Enumerable
                .Range(0, 8)
                .Select(i =>
                    Task.Run(() =>
                        stores[i % 2]
                            .TryRecordAsync("raced", now.AddMinutes(5), now, CancellationToken.None)
                            .AsTask()
                    )
                )
        );

        recorded.Count(r => r).Should().Be(1);
    }

    [Test]
    public async Task ExpiredRecords_AreDeleted()
    {
        await using var instance = RunnerInstance();
        var store = instance.GetRequiredService<INonceStore>();
        var now = DateTimeOffset.UtcNow;
        await store.TryRecordAsync("expired", now.AddMinutes(-1), now, CancellationToken.None);

        for (var i = 0; i < DatabaseNonceStore.SweepEvery; i++)
            await store.TryRecordAsync($"live-{i}", now.AddMinutes(5), now, CancellationToken.None);

        (await Scalar("SELECT count(*) FROM runner_nonce WHERE nonce = 'expired'"))
            .Should()
            .Be(0, "the store deletes expired records as it goes");
    }

    [Test]
    public async Task AFailureThatIsNotAConflict_Throws()
    {
        await using var instance = RunnerInstance();
        var store = instance.GetRequiredService<INonceStore>();
        var now = DateTimeOffset.UtcNow;
        (await store.TryRecordAsync("first", now.AddMinutes(5), now, CancellationToken.None))
            .Should()
            .BeTrue();
        // A trigger's refusal is SQLITE_CONSTRAINT like a key conflict, with its own extended
        // code. Read as a conflict, the takeover would match nothing and report a replay.
        await Scalar(
            "CREATE TRIGGER refuse_nonce BEFORE INSERT ON runner_nonce "
                + "BEGIN SELECT RAISE(ABORT, 'refused'); END"
        );

        var act = () =>
            store.TryRecordAsync("second", now.AddMinutes(5), now, CancellationToken.None).AsTask();

        await act.Should()
            .ThrowAsync<DbUpdateException>(
                "only a primary-key conflict reads as a replay; any other failure must surface (see docs/adr/0009-a-runner-shares-its-accepted-nonces-through-the-database.md)"
            );
    }

    private async Task<long> Scalar(string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={_dbPath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync() is long value ? value : 0;
    }
}
