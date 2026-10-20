using System.Text.Json.Nodes;
using AwesomeAssertions;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// A store written before drafts were keyed by machine implements only the machine-less members. The
/// defaults on <see cref="ISnapshotStore"/> read, delete and record through those, and still keep one
/// machine's draft away from another.
/// </summary>
public class SnapshotStoreDefaultMembersTests
{
    private static string Draft(string machine) =>
        new JsonObject
        {
            ["machine"] = machine,
            ["version"] = 1,
            ["state"] = "Locked",
            ["context"] = new JsonObject(),
        }.ToJsonString();

    private static Snapshot Snapshot(string machine) =>
        new()
        {
            Machine = machine,
            Version = 1,
            State = "Locked",
            Context = new JsonObject(),
        };

    [Test]
    public async Task Get_by_machine_returns_a_draft_that_names_that_machine()
    {
        var id = Guid.NewGuid();
        ISnapshotStore store = new MachineLessStore().With("u", id, Draft("turnstile"));

        var stored = await store.Get("u", "turnstile", id);

        stored.Should().NotBeNull();
        stored!.Json.Should().Be(Draft("turnstile"));
    }

    [Test]
    public async Task Get_by_machine_treats_another_machines_draft_as_absent()
    {
        var id = Guid.NewGuid();
        ISnapshotStore store = new MachineLessStore().With("u", id, Draft("note"));

        (await store.Get("u", "turnstile", id)).Should().BeNull();
    }

    [Test]
    public async Task Get_by_machine_of_a_missing_draft_is_absent()
    {
        ISnapshotStore store = new MachineLessStore();

        (await store.Get("u", "turnstile", Guid.NewGuid())).Should().BeNull();
    }

    [TestCase("not json at all", TestName = "Get_by_machine_returns_a_draft_that_is_not_JSON")]
    [TestCase(
        "{\"machine\":5,\"state\":\"Locked\"}",
        TestName = "Get_by_machine_returns_a_draft_whose_machine_is_not_a_string"
    )]
    [TestCase(
        "{\"state\":\"Locked\"}",
        TestName = "Get_by_machine_returns_a_draft_with_no_machine"
    )]
    [TestCase("null", TestName = "Get_by_machine_returns_a_draft_that_is_JSON_null")]
    public async Task A_draft_whose_machine_cannot_be_read_is_returned_for_rehydration_to_refuse(
        string json
    )
    {
        // Returned rather than hidden: absent, a save would silently overwrite it.
        var id = Guid.NewGuid();
        ISnapshotStore store = new MachineLessStore().With("u", id, json);

        var stored = await store.Get("u", "turnstile", id);

        stored.Should().NotBeNull();
        stored!.Json.Should().Be(json);
    }

    [Test]
    public async Task Delete_by_machine_deletes_that_machines_draft()
    {
        var id = Guid.NewGuid();
        var store = new MachineLessStore().With("u", id, Draft("turnstile"));

        await ((ISnapshotStore)store).Delete("u", "turnstile", id);

        store.Contains("u", id).Should().BeFalse();
    }

    [Test]
    public async Task Delete_by_machine_leaves_another_machines_draft()
    {
        var id = Guid.NewGuid();
        var store = new MachineLessStore().With("u", id, Draft("note"));

        await ((ISnapshotStore)store).Delete("u", "turnstile", id);

        store.Contains("u", id).Should().BeTrue();
    }

    [Test]
    public async Task UpdateWithRequest_records_the_request_id_alone()
    {
        var id = Guid.NewGuid();
        var store = new MachineLessStore().With("u", id, Draft("turnstile"));
        ISnapshotStore contract = store;
        var token = (await contract.Get("u", id))!.Token;

        var written = await contract.UpdateWithRequest(
            "u",
            id,
            Snapshot("turnstile"),
            token,
            new AppliedRequest("req-1", "Coin", "Locked")
        );

        written.Should().BeTrue();
        var stored = (await contract.Get("u", id))!;
        stored.LastRequestId.Should().Be("req-1");
        stored.LastRequest.Should().Be(new AppliedRequest("req-1", null, null));
    }

    [Test]
    public async Task UpdateWithRequest_without_a_request_clears_the_request_id()
    {
        var id = Guid.NewGuid();
        var store = new MachineLessStore().With("u", id, Draft("turnstile"), "req-0");
        ISnapshotStore contract = store;
        var token = (await contract.Get("u", id))!.Token;

        (await contract.UpdateWithRequest("u", id, Snapshot("turnstile"), token, request: null))
            .Should()
            .BeTrue();

        (await contract.Get("u", id))!.LastRequest.Should().BeNull();
    }

    /// <summary>An in-memory store implementing only the members every store must provide.</summary>
    private sealed class MachineLessStore : ISnapshotStore
    {
        private readonly Dictionary<(string, Guid), StoredSnapshot> _rows = new();

        public MachineLessStore With(string userKey, Guid id, string json, string? requestId = null)
        {
            _rows[(userKey, id)] = new StoredSnapshot(
                json,
                Guid.NewGuid(),
                requestId,
                DateTimeOffset.UtcNow
            );
            return this;
        }

        public bool Contains(string userKey, Guid id) => _rows.ContainsKey((userKey, id));

        public Task<StoredSnapshot?> Get(
            string userKey,
            Guid id,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(_rows.GetValueOrDefault((userKey, id)));

        public Task Delete(string userKey, Guid id, CancellationToken cancellationToken = default)
        {
            _rows.Remove((userKey, id));
            return Task.CompletedTask;
        }

        public Task<bool> Upsert(
            string userKey,
            Guid id,
            Snapshot snapshot,
            CancellationToken cancellationToken = default
        )
        {
            With(userKey, id, Draft(snapshot.Machine));
            return Task.FromResult(true);
        }

        public Task<bool> Update(
            string userKey,
            Guid id,
            Snapshot snapshot,
            Guid expectedToken,
            string? requestId = null,
            CancellationToken cancellationToken = default
        )
        {
            if (!_rows.TryGetValue((userKey, id), out var row) || row.Token != expectedToken)
                return Task.FromResult(false);

            With(userKey, id, Draft(snapshot.Machine), requestId);
            return Task.FromResult(true);
        }
    }
}
