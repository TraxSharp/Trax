using System.Text.Json.Nodes;
using AwesomeAssertions;
using Npgsql;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// A row records its last <see cref="RequestIdRing.Capacity"/> client requests with the result each was answered
/// with. Only a client request takes a place: a write the server makes (an outcome, a host's own update) never
/// evicts one, and the seventeenth client request evicts the oldest.
/// <para>Enforces <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
public class RequestIdRingTests
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private const string User = "ring-user";

    [Test]
    public async Task Server_writes_never_evict_the_ring_and_a_seventeenth_client_request_evicts_the_oldest()
    {
        var id = Guid.NewGuid();
        (
            await TestTurnstile
                .Service(TestDb.NewStore())
                .Autosave(User, id, TestTurnstile.InitialJson)
        )
            .Should()
            .BeOfType<AutosaveResult.Saved>();

        for (var i = 1; i <= RequestIdRing.Capacity; i++)
        {
            await ClientRequest(id, i);
            await ServerWrite(id);
        }

        (await Ring(id))
            .Should()
            .BeEquivalentTo(
                Enumerable.Range(1, RequestIdRing.Capacity).Select(RequestId),
                "sixteen client requests fill the ring, and the server writes between them take no place, per "
                    + Adr
            );

        await ClientRequest(id, RequestIdRing.Capacity + 1);

        (await Ring(id))
            .Should()
            .BeEquivalentTo(
                Enumerable.Range(2, RequestIdRing.Capacity).Select(RequestId),
                "the seventeenth client request evicts the oldest"
            );
    }

    private static string RequestId(int n) => $"ring-request-{n}";

    // Odd requests pay, even ones push, so each fires from the state the previous one left.
    private static async Task ClientRequest(Guid id, int n)
    {
        var outcome = await TestTurnstile
            .Service(TestDb.NewStore())
            .Advance(
                User,
                id,
                n % 2 == 1 ? nameof(TurnstileTrigger.Coin) : nameof(TurnstileTrigger.Push),
                n % 2 == 1 ? new JsonObject { ["coin"] = "quarter" } : null,
                RequestId(n)
            );
        outcome.Should().BeOfType<AdvanceOutcome.Advanced>($"request {n} fires");
    }

    // A write no client request made, as an outcome or a host's own update is.
    private static async Task ServerWrite(Guid id)
    {
        await using var connection = new NpgsqlConnection(PostgresSetup.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE trax.snapshot_draft SET concurrency_token = gen_random_uuid(), updated_at = now() "
                + "WHERE user_key = @user AND id = @id",
            connection
        );
        command.Parameters.AddWithValue("user", User);
        command.Parameters.AddWithValue("id", id);
        (await command.ExecuteNonQueryAsync()).Should().Be(1);
    }

    private static async Task<IEnumerable<string>> Ring(Guid id)
    {
        var stored = await TestDb.NewStore().Get(User, id);
        stored.Should().NotBeNull();
        stored!.RecentRequests.Should().NotBeNull("the row records its recent client requests");
        return stored.RecentRequests!.Select(r => r.RequestId);
    }
}
