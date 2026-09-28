using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.RequestSigning;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.JobRunner;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// Two runner instances on one Postgres database share the nonces they accept, so a signed request
/// is accepted once across both. Enforces
/// <c>docs/adr/0009-a-runner-shares-its-accepted-nonces-through-the-database.md</c>.
/// </summary>
[Property("adr", "docs/adr/0009-a-runner-shares-its-accepted-nonces-through-the-database.md")]
[TestFixture]
public class SharedNonceStoreTests
{
    private static readonly byte[] Key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();

    private static ServiceProvider RunnerInstance() =>
        new ServiceCollection()
            .AddLogging(x => x.SetMinimumLevel(LogLevel.Warning))
            .AddTrax(trax =>
                trax.AddEffects(effects => effects.UsePostgres(TestPostgres.ConnectionString))
                    .AddMediator(typeof(AssemblyMarker).Assembly, typeof(JobRunnerTrain).Assembly)
            )
            .AddTraxJobRunner(runner => runner.SigningKey = Key)
            .BuildServiceProvider();

    private static Task<RunnerRequestVerdict> Verify(
        ServiceProvider instance,
        byte[] body,
        string signature
    ) =>
        instance
            .GetRequiredService<RunnerRequestVerifier>()
            .VerifyAsync(RunnerRequestPurpose.Run, body, signature, requireFresh: true)
            .AsTask();

    [Test]
    public async Task ARequestAcceptedByOneInstance_IsRefusedByTheOther()
    {
        await using var first = RunnerInstance();
        await using var second = RunnerInstance();
        var body = Encoding.UTF8.GetBytes($$"""{"trainName":"{{Guid.NewGuid()}}"}""");
        var signature = RunnerRequestSignature.Create(Key, RunnerRequestPurpose.Run, body);

        (await Verify(first, body, signature)).Should().Be(RunnerRequestVerdict.Accepted);

        (await Verify(second, body, signature))
            .Should()
            .Be(
                RunnerRequestVerdict.Replayed,
                "the second instance must see the nonce the first one accepted (see docs/adr/0009-a-runner-shares-its-accepted-nonces-through-the-database.md)"
            );
    }

    [Test]
    public async Task TwoInstancesRacingOneRequest_AcceptItOnce()
    {
        await using var first = RunnerInstance();
        await using var second = RunnerInstance();
        var body = Encoding.UTF8.GetBytes($$"""{"trainName":"{{Guid.NewGuid()}}"}""");
        var signature = RunnerRequestSignature.Create(Key, RunnerRequestPurpose.Run, body);

        var verdicts = await Task.WhenAll(
            Enumerable
                .Range(0, 8)
                .Select(i => Task.Run(() => Verify(i % 2 == 0 ? first : second, body, signature)))
        );

        verdicts.Count(v => v == RunnerRequestVerdict.Accepted).Should().Be(1);
        verdicts
            .Where(v => v != RunnerRequestVerdict.Accepted)
            .Should()
            .AllBeEquivalentTo(RunnerRequestVerdict.Replayed);
    }

    [Test]
    public async Task DistinctRequests_AreEachAccepted()
    {
        await using var first = RunnerInstance();
        await using var second = RunnerInstance();

        for (var i = 0; i < 4; i++)
        {
            var body = Encoding.UTF8.GetBytes($$"""{"n":{{i}}}""");
            var signature = RunnerRequestSignature.Create(Key, RunnerRequestPurpose.Run, body);
            (await Verify(i % 2 == 0 ? first : second, body, signature))
                .Should()
                .Be(RunnerRequestVerdict.Accepted);
        }
    }

    [Test]
    public async Task AnExpiredRecord_IsTakenOver()
    {
        await using var instance = RunnerInstance();
        var store = instance.GetRequiredService<INonceStore>();
        var nonce = Convert.ToHexString(Guid.NewGuid().ToByteArray());
        var now = DateTimeOffset.UtcNow;

        (await store.TryRecordAsync(nonce, now.AddMinutes(-1), now, CancellationToken.None))
            .Should()
            .BeTrue();
        (await store.TryRecordAsync(nonce, now.AddMinutes(5), now, CancellationToken.None))
            .Should()
            .BeTrue("a record past its expiry refuses nothing");
        (await store.TryRecordAsync(nonce, now.AddMinutes(5), now, CancellationToken.None))
            .Should()
            .BeFalse();
    }

    [Test]
    public async Task ExpiredRecords_AreDeleted()
    {
        await using var instance = RunnerInstance();
        var store = instance.GetRequiredService<INonceStore>();
        var now = DateTimeOffset.UtcNow;
        var expired = Convert.ToHexString(Guid.NewGuid().ToByteArray());
        await store.TryRecordAsync(expired, now.AddMinutes(-1), now, CancellationToken.None);

        for (var i = 0; i < DatabaseNonceStore.SweepEvery; i++)
            await store.TryRecordAsync(
                Convert.ToHexString(Guid.NewGuid().ToByteArray()),
                now.AddMinutes(5),
                now,
                CancellationToken.None
            );

        await using var connection = new Npgsql.NpgsqlConnection(TestPostgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM trax.runner_nonce WHERE nonce = @n";
        command.Parameters.AddWithValue("n", expired);
        ((long)(await command.ExecuteScalarAsync())!)
            .Should()
            .Be(0, "the store deletes expired records as it goes");
    }
}
