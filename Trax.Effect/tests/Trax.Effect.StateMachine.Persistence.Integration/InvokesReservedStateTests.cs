using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.Effect.Enums;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// An outcome cannot be forged, on a user-owned machine too: invoking states and every outcome target are reserved,
/// so autosave cannot enter or leave an invoking state or enter an outcome target, <c>advanceSnapshot</c> refuses
/// the outcome triggers, and the correlation lives in a server-only token no context rewrite reaches. See
/// <c>Trax.Docs/adr/0046-a-machine-state-invokes-a-train-and-only-that-entry-receives-its-outcome.md</c>.
/// </summary>
[TestFixture(StoreProvider.Postgres)]
[TestFixture(StoreProvider.Sqlite)]
public class InvokesReservedStateTests(StoreProvider provider)
{
    private const string Adr =
        "Trax.Docs/adr/0046-a-machine-state-invokes-a-train-and-only-that-entry-receives-its-outcome.md";

    private const string User = "u1";

    private InstanceHost _host = null!;
    private RecordingLauncher _launcher = null!;

    [SetUp]
    public void SetUp()
    {
        _launcher = new RecordingLauncher();
        _host = InstanceHost.Create(provider, launcher: _launcher);
    }

    [TearDown]
    public void TearDown() => _host.Dispose();

    private static JsonObject Source => new() { ["source"] = "repo" };

    private static JsonObject BuiltContext => new() { ["source"] = "repo", ["artifact"] = "a1" };

    private async Task<Guid> DraftInBuilding()
    {
        var id = Guid.NewGuid();
        using var scope = _host.Scope();
        var service = _host.Service(scope, BuildMachine.Id);
        (await service.Autosave(User, id, BuildMachine.Json("Draft", Source)))
            .Should()
            .BeOfType<AutosaveResult.Saved>();
        (await service.Advance(User, id, "Build"))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>()
            .Which.Snapshot.State.Should()
            .Be("Building");
        return id;
    }

    [Test]
    public async Task Autosave_into_an_invoking_state_is_refused()
    {
        var created = Guid.NewGuid();
        var moved = Guid.NewGuid();

        using (var scope = _host.Scope())
        {
            var service = _host.Service(scope, BuildMachine.Id);

            (await service.Autosave(User, created, BuildMachine.Json("Building", Source)))
                .Should()
                .BeOfType<AutosaveResult.Rejected>()
                .Which.Code.Should()
                .Be(
                    "state-reserved",
                    $"a save never creates a draft in an invoking state. See {Adr}"
                );

            await service.Autosave(User, moved, BuildMachine.Json("Draft", Source));
            (await service.Autosave(User, moved, BuildMachine.Json("Building", Source)))
                .Should()
                .BeOfType<AutosaveResult.Rejected>()
                .Which.Code.Should()
                .Be("state-reserved", "a save never moves a draft into an invoking state");
        }

        (await _host.Rows(created)).Should().BeEmpty();
        (await _host.Rows(moved)).Single().State.Should().Be("Draft");
        _launcher.Launches.Should().BeEmpty("a refused save queues no run");
    }

    [Test]
    public async Task Autosave_out_of_an_invoking_state_is_refused()
    {
        var id = await DraftInBuilding();
        var before = (await _host.Rows(id)).Single();

        using (var scope = _host.Scope())
        {
            var service = _host.Service(scope, BuildMachine.Id);

            (await service.Autosave(User, id, BuildMachine.Json("Draft", Source)))
                .Should()
                .BeOfType<AutosaveResult.Rejected>()
                .Which.Code.Should()
                .Be(
                    "draft-invoking",
                    $"leaving by a save would strand the run; only a declared transition leaves. See {Adr}"
                );

            (await service.Autosave(User, id, BuildMachine.Json("Draft", Source)))
                .Should()
                .BeOfType<AutosaveResult.Rejected>();

            (
                await service.Autosave(
                    User,
                    id,
                    BuildMachine.Json("Building", new JsonObject { ["source"] = "elsewhere" })
                )
            )
                .Should()
                .BeOfType<AutosaveResult.Rejected>(
                    "the context the run was started from cannot be rewritten under it"
                )
                .Which.Code.Should()
                .Be("draft-invoking");

            (await service.Autosave(User, id, BuildMachine.Json("Building", Source)))
                .Should()
                .BeOfType<AutosaveResult.Saved>(
                    "a save identical to the draft is answered as saved"
                );
        }

        var after = (await _host.Rows(id)).Single();
        after.State.Should().Be("Building");
        after.InvokeToken.Should().Be(before.InvokeToken);
        after.ConcurrencyToken.Should().Be(before.ConcurrencyToken, "nothing was written");
    }

    [Test]
    public async Task Autosave_into_an_outcome_target_is_refused()
    {
        var id = Guid.NewGuid();

        using (var scope = _host.Scope())
        {
            var service = _host.Service(scope, BuildMachine.Id);
            await service.Autosave(User, id, BuildMachine.Json("Draft", Source));

            foreach (
                var (state, context) in new[]
                {
                    ("Built", BuiltContext),
                    ("BuildFailed", Source),
                    ("BuildCancelled", Source),
                }
            )
            {
                (await service.Autosave(User, id, BuildMachine.Json(state, context)))
                    .Should()
                    .BeOfType<AutosaveResult.Rejected>()
                    .Which.Code.Should()
                    .Be(
                        "state-reserved",
                        $"only the run's outcome enters {state}, with what the run produced. See {Adr}"
                    );
                (await service.Autosave(User, Guid.NewGuid(), BuildMachine.Json(state, context)))
                    .Should()
                    .BeOfType<AutosaveResult.Rejected>();
            }
        }

        (await _host.Rows(id)).Single().State.Should().Be("Draft");
    }

    [Test]
    public async Task Advance_with_an_outcome_trigger_is_refused()
    {
        var id = await DraftInBuilding();

        using (var scope = _host.Scope())
        {
            var service = _host.Service(scope, BuildMachine.Id);

            foreach (
                var trigger in new[] { "Building.done", "Building.failed", "Building.cancelled" }
            )
                (
                    await service.Advance(
                        User,
                        id,
                        trigger,
                        new JsonObject { ["artifact"] = "forged" }
                    )
                )
                    .Should()
                    .BeOfType<AdvanceOutcome.Rejected>()
                    .Which.Reason.Should()
                    .Be(
                        "outcome-bound",
                        $"an outcome is applied only from its run, never by an advance. See {Adr}"
                    );
        }

        (await _host.Rows(id)).Single().State.Should().Be("Building");
    }

    [Test]
    public async Task A_rewritten_context_does_not_change_correlation()
    {
        var id = await DraftInBuilding();
        var token = (await _host.Rows(id)).Single().InvokeToken;
        token.Should().NotBeNull().And.Be(_launcher.Launches.Single().ExternalId);

        using (var scope = _host.Scope())
        {
            var service = _host.Service(scope, BuildMachine.Id);
            var forged = new JsonObject { ["source"] = "repo", ["invokeToken"] = "forged" };
            (await service.Autosave(User, id, BuildMachine.Json("Building", forged)))
                .Should()
                .BeOfType<AutosaveResult.Rejected>();

            var loaded = (LoadResult.Loaded)await service.Load(User, id);
            service
                .Serialize(loaded.Snapshot)
                .Should()
                .NotContain(token!, $"the token is server-only, never in the snapshot. See {Adr}");

            (await _host.Instances(scope).GetByInvokeToken("forged")).Should().BeNull();
            (await _host.Instances(scope).GetByInvokeToken(token!))
                .Should()
                .NotBeNull()
                .And.Match<StoredInstance>(x =>
                    x.Id == id && x.Owner.Kind == SnapshotOwnerKind.User && x.Owner.UserKey == User
                );
        }

        (await _host.Rows(id)).Single().InvokeToken.Should().Be(token);
    }
}
