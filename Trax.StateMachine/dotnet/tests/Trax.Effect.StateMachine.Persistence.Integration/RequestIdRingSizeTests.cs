using System.Text;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// The request ring is bounded in bytes as well as in entries: however large the results it records, a row's ring
/// stays within <see cref="RequestIdRing.MaxBytes"/>, the oldest entries going first, and the newest request is always
/// kept, so a retry of it still replays.
/// </summary>
[TestFixture(StoreProvider.Postgres)]
[TestFixture(StoreProvider.Sqlite)]
[Property("adr", Adr)]
public class RequestIdRingSizeTests(StoreProvider provider)
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private const string User = "ring-size-user";

    // A context near the snapshot cap, so every result the ring records is large.
    private static readonly string Source = new('x', 50 * 1024);

    private InstanceHost _host = null!;

    [SetUp]
    public void SetUp() => _host = InstanceHost.Create(provider, launcher: new RecordingLauncher());

    [TearDown]
    public void TearDown() => _host.Dispose();

    private async Task<AdvanceOutcome> Edit(Guid id, string requestId)
    {
        using var scope = _host.Scope();
        return await _host
            .Service(scope, BuildMachine.Id)
            .Advance(User, id, nameof(BuildTrigger.Edit), null, requestId);
    }

    [Test]
    public async Task A_row_after_sixteen_large_advances_keeps_its_ring_within_the_byte_bound()
    {
        var id = Guid.NewGuid();
        await _host.SeedDraft(
            User,
            id,
            BuildMachine.Json(nameof(BuildState.Draft), new JsonObject { ["source"] = Source })
        );

        for (var i = 1; i <= RequestIdRing.Capacity; i++)
            (await Edit(id, $"edit-{i}")).Should().BeOfType<AdvanceOutcome.Advanced>();

        var column = (string?)
            await _host.Scalar(
                provider == StoreProvider.Postgres
                    ? $"SELECT recent_requests::text FROM {_host.DraftTable} WHERE row_id = @p0"
                    : $"SELECT recent_requests FROM {_host.DraftTable} WHERE row_id = @p0",
                await _host.RowId(id)
            );
        column.Should().NotBeNull();
        var ring = RequestIdRing.Parse(column)!;
        Encoding
            .UTF8.GetByteCount(RequestIdRing.Serialize(ring))
            .Should()
            .BeLessThanOrEqualTo(
                RequestIdRing.MaxBytes,
                $"sixteen results of 50 KiB would be 800 KiB; the ring keeps the newest that fit. See {Adr}"
            );
        // jsonb prints a space after each separator between the ring's own fields; the results are strings.
        Encoding
            .UTF8.GetByteCount(column!)
            .Should()
            .BeLessThanOrEqualTo(RequestIdRing.MaxBytes + 1024);
        ring.Select(r => r.RequestId).Should().Contain($"edit-{RequestIdRing.Capacity}");
        ring.Should()
            .HaveCountLessThan(RequestIdRing.Capacity, "the oldest large results were evicted");

        var revision = await _host.Revision(id);
        (await Edit(id, $"edit-{RequestIdRing.Capacity}"))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>()
            .Which.Revision.Should()
            .Be(revision, "the newest request is always kept, so its retry replays");
        (await _host.Revision(id)).Should().Be(revision);
    }

    // About 20 KiB of canonical wire each: a quote in every four characters, and three-byte characters.
    private static readonly string Quoted = string.Concat(Enumerable.Repeat("say \"hi\" ", 1850));
    private static readonly string Cjk = string.Concat(Enumerable.Repeat("状态机的请求", 1130));

    // A quote inside a result is written \" in the wire and \\\" in the ring's text, so a result that is a third
    // quotes and backslashes takes about 28 KiB there: four fit. Three-byte characters are written as they are: six.
    [TestCase("quoted", 4)]
    [TestCase("cjk", 6)]
    public async Task A_ring_of_twenty_KiB_results_keeps_as_many_as_fit_in_the_stored_text(
        string kind,
        int fits
    )
    {
        var id = Guid.NewGuid();
        var value = kind == "quoted" ? Quoted : Cjk;
        var snapshot = BuildMachine.Json(
            nameof(BuildState.Draft),
            new JsonObject { ["source"] = value }
        );
        await _host.SeedDraft(User, id, snapshot);

        for (var i = 1; i <= RequestIdRing.Capacity; i++)
            (await Edit(id, $"edit-{i}")).Should().BeOfType<AdvanceOutcome.Advanced>();

        var ring = RequestIdRing.Parse(await StoredText(id))!;
        var result = ring[^1].ResultSnapshot;
        Encoding.UTF8.GetByteCount(result).Should().BeInRange(19 * 1024, 21 * 1024);

        // The byte math: the kept entries, with the array's brackets and commas, fit the bound, and one more of the
        // same size would not.
        var sizes = ring.Select(RequestIdRing.SizeOf).ToList();
        var kept = 2 + sizes.Sum() + (sizes.Count - 1);
        kept.Should().BeLessThanOrEqualTo(RequestIdRing.MaxBytes);
        (kept + sizes.Max() + 1)
            .Should()
            .BeGreaterThan(
                RequestIdRing.MaxBytes,
                $"each entry takes about {sizes[^1]} bytes as stored, and the ring keeps as many as fit; see {Adr}"
            );
        var expected = ring.Count;
        expected.Should().Be(fits, "a result is counted as the column's text stores it");
        ring.Select(r => r.RequestId)
            .Should()
            .Equal(
                Enumerable
                    .Range(RequestIdRing.Capacity - expected + 1, expected)
                    .Select(i => $"edit-{i}")
            );

        // What the column holds is no larger than what was counted.
        var storedBytes = Convert.ToInt64(
            await _host.Scalar(
                provider == StoreProvider.Postgres
                    ? $"SELECT sum(octet_length(e->>'result')) FROM {_host.DraftTable}, jsonb_array_elements(recent_requests) e WHERE row_id = @p0"
                    : $"SELECT length(CAST(recent_requests AS BLOB)) FROM {_host.DraftTable} WHERE row_id = @p0",
                await _host.RowId(id)
            ),
            System.Globalization.CultureInfo.InvariantCulture
        );
        storedBytes.Should().BeLessThanOrEqualTo(RequestIdRing.MaxBytes);
        if (provider == StoreProvider.Sqlite)
            storedBytes
                .Should()
                .Be(
                    Encoding.UTF8.GetByteCount(RequestIdRing.Serialize(ring)),
                    "SQLite stores exactly the text that was measured"
                );
    }

    [Test]
    public async Task A_ring_written_with_every_character_escaped_still_replays()
    {
        var id = Guid.NewGuid();
        await _host.SeedDraft(
            User,
            id,
            BuildMachine.Json(nameof(BuildState.Draft), new JsonObject { ["source"] = Cjk })
        );
        var revision = (await Edit(id, "edit-1"))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>()
            .Which.Revision;

        // The ring as an earlier version wrote it: the default encoder escapes every quote and non-ASCII character.
        var ring = RequestIdRing.Parse(await StoredText(id))!;
        var escaped = System.Text.Json.JsonSerializer.Serialize(
            ring.Select(r => new
                {
                    requestId = r.RequestId,
                    fingerprint = r.Fingerprint,
                    revision = r.Revision,
                    result = r.ResultSnapshot,
                    trigger = r.Trigger,
                    fromState = r.FromState,
                })
                .ToList()
        );
        escaped.Should().Contain("\\u0022");
        await _host.Execute(
            provider == StoreProvider.Postgres
                ? $"UPDATE {_host.DraftTable} SET recent_requests = CAST(@p0 AS jsonb) WHERE row_id = @p1"
                : $"UPDATE {_host.DraftTable} SET recent_requests = @p0 WHERE row_id = @p1",
            escaped,
            await _host.RowId(id)
        );

        // The raw write moved the row's revision; a replay leaves it where it is.
        var written = await _host.Revision(id);
        (await Edit(id, "edit-1"))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>()
            .Which.Revision.Should()
            .Be(revision, "the recorded request replays from a ring in the earlier form");
        (await _host.Revision(id)).Should().Be(written);
    }

    private async Task<string> StoredText(Guid id) =>
        (string)
            (
                await _host.Scalar(
                    provider == StoreProvider.Postgres
                        ? $"SELECT recent_requests::text FROM {_host.DraftTable} WHERE row_id = @p0"
                        : $"SELECT recent_requests FROM {_host.DraftTable} WHERE row_id = @p0",
                    await _host.RowId(id)
                )
            )!;
}
