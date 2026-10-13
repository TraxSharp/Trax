using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using CsCheck;
using Npgsql;
using Trax.Effect.StateMachine.Persistence;
using Trax.Effect.StateMachine.Tests.Stress.Fakes;
using Trax.Effect.StateMachine.Tests.Stress.Fixtures;

namespace Trax.Effect.StateMachine.Tests.Stress.IntegrationTests;

/// <summary>
/// The revision under contention, against <see cref="EfSnapshotStore"/> on Postgres and an in-memory model of each
/// incarnation of one instance. A generated sequence of incarnations each starts the instance with racing inserts,
/// then races a burst of advances, blind upserts and writes from an older node that leaves the revision out, then
/// deletes it. After each burst: the incarnation's revision is one plus the writes that committed, none repeated or
/// skipped; the older node's write was bumped by the database; replaying the model in revision order gives the final
/// row; and a re-created instance is a new <c>row_id</c> that starts again at 1. See
/// <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c> and
/// <c>Trax.Docs/adr/0044-property-tests-use-cscheck-in-test-projects-only.md</c>.
/// </summary>
/// <remarks>
/// No timer fires here: the stress order has no timed state. The interleaving inside a burst is the database's, so
/// a failing seed replays the operations, not necessarily their order.
/// </remarks>
[TestFixture]
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
public class RevisionStoreModelTests : StressFixture
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private const string User = "u";

    private const int Iterations = 20;

    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);

    private enum Kind
    {
        Advance,
        Upsert,
        OldNodeWrite,
    }

    private readonly record struct Op(Kind Kind, int Value)
    {
        public override string ToString() => $"{Kind}({Value})";
    }

    private static readonly Gen<Op> AnOperation = Gen.Frequency(
        (3, Gen.Int[1, 9].Select(v => new Op(Kind.Advance, v))),
        (3, Gen.Int[1, 9].Select(v => new Op(Kind.Upsert, v))),
        (2, Gen.Int[1, 9].Select(v => new Op(Kind.OldNodeWrite, v)))
    );

    // Each incarnation: how many inserts race to create it, and the burst that follows.
    private static readonly Gen<(int Starters, Op[] Burst)> AnIncarnation = Gen.Select(
        Gen.Int[1, 4],
        AnOperation.Array[2, 16]
    );

    [Test]
    public Task Every_incarnation_commits_revisions_one_to_n_and_replays_to_its_final_row() =>
        AnIncarnation
            .Array[1, 4]
            .SampleAsync(
                async incarnations => await Run(incarnations).WaitAsync(Bound),
                iter: Iterations,
                threads: 1,
                print: incarnations =>
                    string.Join(
                        Environment.NewLine,
                        incarnations.Select(
                            (x, i) =>
                                $"  {i}: {x.Starters} starts, then {string.Join(", ", x.Burst)}"
                        )
                    )
            );

    /// <summary>A write that committed, and the revision it reported where it reports one.</summary>
    private sealed record Committed(Op Op, long? Revision);

    private static async Task Run((int Starters, Op[] Burst)[] incarnations)
    {
        var id = Guid.NewGuid();
        var rowIds = new List<long>();

        foreach (var (starters, burst) in incarnations)
        {
            // Racing starts: one creates the row, the rest find it there.
            var created = await Task.WhenAll(
                Enumerable
                    .Range(0, starters)
                    .Select(_ =>
                        Task.Run(async () =>
                        {
                            await using var ctx = StressDb.NewContext();
                            return await ((ISnapshotStore)StressDb.NewStore(ctx)).Insert(
                                User,
                                id,
                                Draft(0)
                            );
                        })
                    )
            );
            created.Count(c => c).Should().Be(1, "one insert creates the incarnation");

            var (rowId, start, _) = await ReadRow(id);
            start.Should().Be(1, $"a new incarnation starts at revision 1. See {Adr}");
            rowIds.Should().NotContain(rowId, "a re-created instance is a new incarnation");
            rowIds.Add(rowId);

            var committed = new ConcurrentBag<Committed>();
            await Task.WhenAll(burst.Select(op => Task.Run(() => Apply(id, op, committed))));

            var (finalRowId, revision, row) = await ReadRow(id);
            finalRowId.Should().Be(rowId, "nothing deleted the row during the burst");
            revision
                .Should()
                .Be(
                    1 + committed.Count,
                    $"each committed write bumped once, the older node's included. See {Adr}"
                );

            var reported = committed.Where(c => c.Revision is not null).ToList();
            reported
                .Select(c => c.Revision!.Value)
                .Should()
                .OnlyHaveUniqueItems("no two writes committed the same revision")
                .And.OnlyContain(r => r >= 2 && r <= revision, "none was skipped past the last");

            // Replaying the model in revision order ends where the database does. The last write reports its
            // revision unless it was a blind upsert, which then wrote the row as it is.
            var last = reported.SingleOrDefault(c => c.Revision == revision);
            if (last is not null)
                Expected(last.Op, row)
                    .Should()
                    .BeTrue($"the write that took revision {revision} left the final row");
            else if (committed.IsEmpty)
                (row["state"]!.GetValue<string>() == "Draft" && Items(row).Length == 0)
                    .Should()
                    .BeTrue("with no write committed, the row is the one the insert wrote");
            else
                committed
                    .Should()
                    .Contain(
                        c => c.Op.Kind == Kind.Upsert && IsDraftOf(row, c.Op.Value),
                        "an unreported last write is an upsert, and the row is what it wrote"
                    );

            await using (var ctx = StressDb.NewContext())
                await StressDb.NewStore(ctx).Delete(User, id);
        }
    }

    private static async Task Apply(Guid id, Op op, ConcurrentBag<Committed> committed)
    {
        switch (op.Kind)
        {
            case Kind.Advance:
            {
                await using var ctx = StressDb.NewContext();
                if (
                    await StressOrder.Service(StressDb.NewStore(ctx)).Advance(User, id, "Next")
                    is AdvanceOutcome.Advanced advanced
                )
                    committed.Add(new Committed(op, advanced.Revision));
                return;
            }
            case Kind.Upsert:
            {
                await using var ctx = StressDb.NewContext();
                if (await StressDb.NewStore(ctx).Upsert(User, id, Draft(op.Value)))
                    committed.Add(new Committed(op, null));
                return;
            }
            case Kind.OldNodeWrite:
            {
                // An older node writes the context and a fresh token, and knows nothing of the revision.
                await using var connection = new NpgsqlConnection(StressDb.ConnectionString);
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    UPDATE trax.snapshot_draft
                    SET context = jsonb_build_object('items', jsonb_build_array(@item), 'receipt', NULL),
                        concurrency_token = gen_random_uuid(),
                        updated_at = now()
                    WHERE owner_kind = 'user' AND user_key = @user AND machine = 'order' AND id = @id
                    RETURNING revision
                    """;
                command.Parameters.AddWithValue("item", op.Value);
                command.Parameters.AddWithValue("user", User);
                command.Parameters.AddWithValue("id", id);
                if (await command.ExecuteScalarAsync() is long revision)
                    committed.Add(new Committed(op, revision));
                return;
            }
        }
    }

    // What the row must hold if op was the last write to it.
    private static bool Expected(Op op, JsonObject row) =>
        op.Kind switch
        {
            Kind.Advance => row["state"]!.GetValue<string>() == "Review",
            Kind.OldNodeWrite => Items(row) is [var only] && only == op.Value,
            _ => IsDraftOf(row, op.Value),
        };

    private static bool IsDraftOf(JsonObject row, int item) =>
        row["state"]!.GetValue<string>() == "Draft" && Items(row) is [var only] && only == item;

    private static int[] Items(JsonObject row) =>
        (row["context"]!["items"] as JsonArray)!.Select(n => n!.GetValue<int>()).ToArray();

    private static Snapshot Draft(int item)
    {
        var items = new JsonArray();
        if (item > 0)
            items.Add(item);
        return new Snapshot
        {
            Machine = "order",
            Version = 1,
            State = "Draft",
            Context = new JsonObject { ["items"] = items, ["receipt"] = null },
        };
    }

    // The row's surrogate key, its revision and its state and context, read in one statement.
    private static async Task<(long RowId, long Revision, JsonObject Row)> ReadRow(Guid id)
    {
        await using var connection = new NpgsqlConnection(StressDb.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT row_id, revision, state, context::text
            FROM trax.snapshot_draft
            WHERE owner_kind = 'user' AND user_key = @user AND machine = 'order' AND id = @id
            """;
        command.Parameters.AddWithValue("user", User);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue("the instance exists");
        return (
            reader.GetInt64(0),
            reader.GetInt64(1),
            new JsonObject
            {
                ["state"] = reader.GetString(2),
                ["context"] = JsonNode.Parse(reader.GetString(3)),
            }
        );
    }
}
