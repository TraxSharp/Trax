using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;
using Trax.Effect.StateMachine.Persistence.Mutations;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// A request id is recorded on the row, so one holding a control character is refused as <c>invalid-request-id</c>
/// on every path, before anything is read, written or run: Postgres cannot store a NUL in text at all, and no other
/// control character belongs in an id. Without the check a send ran its effect and then failed to record it.
/// </summary>
[TestFixture(StoreProvider.Postgres)]
[TestFixture(StoreProvider.Sqlite)]
[Property("adr", Adr)]
public class RequestIdControlCharacterTests(StoreProvider provider)
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private const string User = "control-id-user";

    // Named by what the id holds: a test name with the character itself is not one every runner can report.
    private static IEnumerable<TestCaseData> Ids() =>
        [
            new TestCaseData("a\0b", "NUL").SetArgDisplayNames("NUL"),
            new TestCaseData("\0", "NUL alone").SetArgDisplayNames("NUL alone"),
            new TestCaseData("a\u0001b", "SOH").SetArgDisplayNames("SOH"),
            new TestCaseData("a\nb", "line feed").SetArgDisplayNames("line feed"),
            new TestCaseData("a\tb", "tab").SetArgDisplayNames("tab"),
            new TestCaseData("a\u001fb", "unit separator").SetArgDisplayNames("unit separator"),
            new TestCaseData("a\u007fb", "DEL").SetArgDisplayNames("DEL"),
            new TestCaseData("a\u0085b", "next line (C1)").SetArgDisplayNames("next line (C1)"),
            new TestCaseData("a\ud800b", "lone high surrogate").SetArgDisplayNames(
                "lone high surrogate"
            ),
            new TestCaseData("a\udc00b", "lone low surrogate").SetArgDisplayNames(
                "lone low surrogate"
            ),
        ];

    private InstanceHost _host = null!;
    private SharedCharge _charge = null!;

    [SetUp]
    public void SetUp()
    {
        _charge = new SharedCharge();
        _host = InstanceHost.Create(
            provider,
            launcher: new RecordingLauncher(),
            services: services => services.AddSingleton<IOrderCharge>(_charge)
        );
    }

    [TearDown]
    public void TearDown() => _host.Dispose();

    private static string Because(string name) =>
        $"a request id with a control character ({name}) names no request the row can record. See {Adr}";

    private ISnapshotMachineRegistry Registry(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<ISnapshotMachineRegistry>();

    [TestCaseSource(nameof(Ids))]
    public async Task A_start_is_refused_and_creates_nothing(string requestId, string name)
    {
        var user = $"{User}-{Guid.NewGuid():N}";
        StartOutcome outcome;
        StartSnapshotOutput generic;
        using (var scope = _host.Scope())
        {
            outcome = await _host
                .Service(scope, ExpiringMachine.Id)
                .Start(new Actor.Client(user), null, requestId);
            generic = await new StartSnapshotJunction(
                Registry(scope),
                new FakePrincipal(user),
                scope.ServiceProvider
            ).Run(new StartSnapshotInput { Machine = ExpiringMachine.Id, RequestId = requestId });
        }

        outcome
            .Should()
            .BeOfType<StartOutcome.Rejected>()
            .Which.Code.Should()
            .Be(DraftRefusalCodes.InvalidRequestId, Because(name));
        generic.Problem!.Code.Should().Be(DraftRefusalCodes.InvalidRequestId, Because(name));
        (await _host.UserRows(user, ExpiringMachine.Id))
            .Should()
            .BeEmpty("a refused start creates nothing");
    }

    [TestCaseSource(nameof(Ids))]
    public async Task An_advance_is_refused_and_moves_nothing(string requestId, string name)
    {
        var id = Guid.NewGuid();
        await _host.SeedDraft(User, id, PlainTimerMachine.Json("Idle"));
        var before = await _host.Revision(id);

        AdvanceOutcome outcome;
        AdvanceSnapshotOutput generic;
        using (var scope = _host.Scope())
        {
            outcome = await _host
                .Service(scope, PlainTimerMachine.Id)
                .Advance(new Actor.Client(User), id, "Wait", null, requestId);
            generic = await new AdvanceSnapshotJunction(
                Registry(scope),
                new FakePrincipal(User),
                services: scope.ServiceProvider
            ).Run(
                new AdvanceSnapshotInput
                {
                    Machine = PlainTimerMachine.Id,
                    Id = id,
                    Trigger = "Wait",
                    RequestId = requestId,
                }
            );
        }

        outcome
            .Should()
            .BeOfType<AdvanceOutcome.Rejected>()
            .Which.Reason.Should()
            .Be(DraftRefusalCodes.InvalidRequestId, Because(name));
        generic.Problem!.Code.Should().Be(DraftRefusalCodes.InvalidRequestId, Because(name));
        var row = (await RawSql.Timer(_host, id))!;
        row.State.Should().Be("Idle", "a refused advance moves nothing");
        row.Revision.Should().Be(before, "a refused advance writes nothing");
    }

    [TestCaseSource(nameof(Ids))]
    public async Task A_send_is_refused_before_its_effect_runs(string requestId, string name)
    {
        var id = Guid.NewGuid();
        await _host.SeedDraft(User, id, RenewalMachine.DraftJson);
        var before = await _host.Revision(id);

        AdvanceOutcome sent;
        AdvanceOutcome ran;
        SendSnapshotOutput generic;
        using (var scope = _host.Scope())
        {
            sent = await _host
                .Service(scope, RenewalMachine.Id)
                .Send(new Actor.Client(User), id, requestId);
            ran = await Registry(scope).EffectRunner(RenewalMachine.Id)!.Run(User, id, requestId);
            generic = await new SendSnapshotJunction(
                Registry(scope),
                new FakePrincipal(User),
                services: scope.ServiceProvider
            ).Run(
                new SendSnapshotInput
                {
                    Machine = RenewalMachine.Id,
                    Id = id,
                    RequestId = requestId,
                }
            );
        }

        foreach (var outcome in new[] { sent, ran })
            outcome
                .Should()
                .BeOfType<AdvanceOutcome.Rejected>()
                .Which.Reason.Should()
                .Be(DraftRefusalCodes.InvalidRequestId, Because(name));
        generic.Problem!.Code.Should().Be(DraftRefusalCodes.InvalidRequestId, Because(name));
        _charge.Calls.Should().Be(0, "a refused send delivers nothing");
        var row = (await RawSql.Timer(_host, id))!;
        row.State.Should().Be(nameof(RenewalState.Draft), "a refused send moves nothing");
        row.Revision.Should().Be(before, "a refused send writes nothing");
    }

    [TestCaseSource(nameof(Ids))]
    public async Task A_discard_is_refused_and_deletes_nothing(string requestId, string name)
    {
        var id = Guid.NewGuid();
        await _host.SeedDraft(User, id, PlainTimerMachine.Json("Idle"));

        DiscardOutcome outcome;
        DiscardSnapshotOutput generic;
        using (var scope = _host.Scope())
        {
            outcome = await _host
                .Service(scope, PlainTimerMachine.Id)
                .Discard(new Actor.Client(User), id, requestId);
            generic = await new DiscardSnapshotJunction(
                Registry(scope),
                new FakePrincipal(User),
                scope.ServiceProvider
            ).Run(
                new DiscardSnapshotInput
                {
                    Machine = PlainTimerMachine.Id,
                    Id = id,
                    RequestId = requestId,
                }
            );
        }

        outcome
            .Should()
            .BeOfType<DiscardOutcome.Rejected>()
            .Which.Code.Should()
            .Be(DraftRefusalCodes.InvalidRequestId, Because(name));
        generic.Problem!.Code.Should().Be(DraftRefusalCodes.InvalidRequestId, Because(name));
        (await _host.Rows(id)).Should().ContainSingle("a refused discard deletes nothing");
    }

    [TestCase("a-b_c.1", TestName = "{m}(ascii)")]
    [TestCase("ключ-🔑", TestName = "{m}(non-ASCII and a supplementary-plane character)")]
    public async Task An_id_without_a_control_character_is_still_accepted(string requestId)
    {
        var id = Guid.NewGuid();
        await _host.SeedDraft(User, id, PlainTimerMachine.Json("Idle"));

        using var scope = _host.Scope();
        (
            await _host
                .Service(scope, PlainTimerMachine.Id)
                .Advance(new Actor.Client(User), id, "Wait", null, requestId)
        )
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>();
    }
}
