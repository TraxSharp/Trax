using System.Reflection;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using CsCheck;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// Every event's rule is checked below the resolver, where every surface meets: a client actor that does not meet
/// it is refused as <c>forbidden</c> before anything is validated, written or launched, on the draft service, on the
/// effect-bound send and from a host train forwarding a caller's event. Host code on a user's draft is a client too;
/// only host code driving a system-owned instance through <see cref="IMachineInstances"/> is not held to the rules.
/// </summary>
[TestFixture(StoreProvider.Postgres)]
[TestFixture(StoreProvider.Sqlite)]
[Property("adr", Adr)]
public class EventAuthorizationTests(StoreProvider provider)
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private const string Unauthenticated = "unauthenticated";

    private InstanceHost _host = null!;
    private AuthorizingLauncher _launcher = null!;

    // One host per fixture: the matrix is large, and every test works on drafts of its own users.
    [OneTimeSetUp]
    public void SetUp()
    {
        _launcher = new AuthorizingLauncher();
        _host = InstanceHost.Create(provider, launcher: _launcher);
    }

    [OneTimeTearDown]
    public void TearDown() => _host.Dispose();

    private static string NewUser(string name = "user") => $"{name}-{Guid.NewGuid():N}";

    private async Task<Guid> DeskIn(string owner, DeskState state)
    {
        var id = Guid.NewGuid();
        await _host.Seed(owner, id, RuledDeskMachine.In(state));
        return id;
    }

    private async Task<string> StateOf(Guid id) => (await _host.Rows(id)).Single().State;

    [Test]
    public async Task A_client_without_the_role_gets_forbidden_and_nothing_is_written()
    {
        var user = NewUser();
        var id = Guid.NewGuid();
        await _host.Seed(user, id, RuledBuildMachine.In(BuildState.Draft));
        var before = (await _host.Rows(id)).Single();
        var queued = await _host.WorkQueueCount();
        var launches = _launcher.Calls;

        AdvanceOutcome outcome;
        using (var scope = _host.Scope())
            outcome = await _host
                .Service(scope, RuledBuildMachine.Id)
                .Advance(DeskRoles.Client(user), id, nameof(BuildTrigger.Build), requestId: "r1");

        outcome
            .Should()
            .BeOfType<AdvanceOutcome.Rejected>()
            .Which.Reason.Should()
            .Be(
                DraftRefusalCodes.Forbidden,
                $"the event's rule is checked before the invoked train's authorization. See {Adr}"
            );
        var after = (await _host.Rows(id)).Single();
        after.State.Should().Be(before.State);
        after.Context.Should().Be(before.Context);
        after.ConcurrencyToken.Should().Be(before.ConcurrencyToken, "nothing was written");
        after.UpdatedAt.Should().Be(before.UpdatedAt);
        after.InvokeToken.Should().BeNull();
        (await _host.WorkQueueCount()).Should().Be(queued, "no run was queued");
        _launcher.Calls.Should().Be(launches, "a forbidden caller never reaches the train's check");
    }

    /// <summary>A caller in the matrix: who it is and the roles it holds.</summary>
    public sealed record Caller(string Name, string[] Roles)
    {
        public bool Authenticated => Name != "none";

        public bool Holds(string role) => Roles.Contains(role, StringComparer.Ordinal);

        public override string ToString() => Name;
    }

    /// <summary>Where the event is sent from.</summary>
    public enum Surface
    {
        /// <summary>The machine's draft service, built over the request's store.</summary>
        DraftService,

        /// <summary>A host train forwarding the caller's event through the registry it resolves.</summary>
        HostTrain,

        /// <summary>The effect-bound send, which names no trigger.</summary>
        Send,
    }

    private static readonly Caller[] Callers =
    [
        new("none", []),
        new("plain user", []),
        new("Operator", [DeskRoles.Operator]),
        new("Admin", [DeskRoles.Admin]),
        new("Operator+Admin", [DeskRoles.Operator, DeskRoles.Admin]),
        new("lower-case operator", ["operator"]),
    ];

    private static readonly DeskTrigger[] EventRules =
    [
        DeskTrigger.OperatorOnly,
        DeskTrigger.OperatorOrAdmin,
        DeskTrigger.PolicyOnly,
        DeskTrigger.Anyone,
    ];

    // The model: Requires("Operator"), Requires("Operator","Admin"), RequiresPolicy (which an Admin satisfies,
    // see DeskRoles.Client) and AnyAuthenticated. Roles compare ordinally, so "operator" is not "Operator".
    private static bool Satisfies(Caller caller, DeskTrigger rule) =>
        caller.Authenticated
        && rule switch
        {
            DeskTrigger.OperatorOnly => caller.Holds(DeskRoles.Operator),
            DeskTrigger.OperatorOrAdmin => caller.Holds(DeskRoles.Operator)
                || caller.Holds(DeskRoles.Admin),
            DeskTrigger.PolicyOnly => caller.Holds(DeskRoles.Admin),
            DeskTrigger.Anyone => true,
            _ => false,
        };

    private static IEnumerable<TestCaseData> Matrix()
    {
        foreach (var caller in Callers)
        {
            foreach (var rule in EventRules)
            foreach (var surface in new[] { Surface.DraftService, Surface.HostTrain })
                yield return new TestCaseData(caller, rule, surface).SetArgDisplayNames(
                    caller.Name,
                    rule.ToString(),
                    surface.ToString()
                );

            // The effect-bound Ship requires Operator, like OperatorOnly.
            yield return new TestCaseData(
                caller,
                DeskTrigger.OperatorOnly,
                Surface.Send
            ).SetArgDisplayNames(caller.Name, "Ship (Operator)", nameof(Surface.Send));
        }
    }

    [TestCaseSource(nameof(Matrix))]
    public async Task Event_rules_across_roles_and_surfaces(
        Caller caller,
        DeskTrigger rule,
        Surface surface
    )
    {
        var owner = NewUser();
        var actor = DeskRoles.Client(caller.Authenticated ? owner : " ", caller.Roles);
        var allowed = Satisfies(caller, rule);
        string[] refusedAs = caller.Authenticated
            ? [DraftRefusalCodes.Forbidden]
            : [DraftRefusalCodes.Forbidden, Unauthenticated];
        var id = Guid.NewGuid();

        if (surface == Surface.Send)
        {
            await _host.Seed(owner, id, RuledShipMachine.Packing);
            AdvanceOutcome sent;
            using (var scope = _host.Scope())
                sent = await _host.Service(scope, RuledShipMachine.Id).Send(actor, id, "send-1");

            if (allowed)
                sent.Should().BeOfType<AdvanceOutcome.Advanced>();
            else
                sent.Should()
                    .BeOfType<AdvanceOutcome.Rejected>()
                    .Which.Reason.Should()
                    .BeOneOf(
                        refusedAs,
                        $"the effect-bound event's rule is checked though send names no trigger. See {Adr}"
                    );
            (await StateOf(id))
                .Should()
                .Be(allowed ? nameof(ShipState.Shipped) : nameof(ShipState.Packing));
            return;
        }

        await _host.Seed(owner, id, RuledDeskMachine.In(DeskState.Open));
        using (var scope = _host.Scope())
        {
            var service =
                surface == Surface.DraftService
                    ? new RuledDeskMachine().CreateService(
                        scope.ServiceProvider.GetRequiredService<ISnapshotStore>(),
                        scope.ServiceProvider.GetRequiredService<IEffectClaimStore>()
                    )
                    : _host.Service(scope, RuledDeskMachine.Id);

            // The input check never runs for a forbidden caller: an input the event does not take is refused as
            // forbidden for them, and as invalid-input only for a caller the rule admits.
            var badInput = await service.Advance(
                actor,
                id,
                rule.ToString(),
                new JsonObject { ["unexpected"] = 1 },
                "bad-input"
            );
            badInput
                .Should()
                .BeOfType<AdvanceOutcome.Rejected>()
                .Which.Reason.Should()
                .BeOneOf(
                    allowed ? [RejectionReasons.InvalidInput] : refusedAs,
                    $"the rule is checked before the input. See {Adr}"
                );

            var outcome = await service.Advance(actor, id, rule.ToString(), null, "ok");
            if (allowed)
                outcome.Should().BeOfType<AdvanceOutcome.Advanced>();
            else
                outcome
                    .Should()
                    .BeOfType<AdvanceOutcome.Rejected>()
                    .Which.Reason.Should()
                    .BeOneOf(refusedAs, $"forbidden exactly when the rule is unmet. See {Adr}");
        }

        (await StateOf(id)).Should().Be(allowed ? nameof(DeskState.Done) : nameof(DeskState.Open));
    }

    [Test]
    public async Task A_trusted_actor_is_not_held_to_event_rules_but_its_input_is_still_validated()
    {
        var key = MachineKey.Of("trusted", Guid.NewGuid().ToString());
        await _host.Start<RuledSystemTurnstileMachine>(key);

        using var scope = _host.Scope();
        var instances = scope.ServiceProvider.GetRequiredService<IMachineInstances>();

        // Coin requires Operator, and host code holds no role: it is not checked.
        (
            await instances.Advance<RuledSystemTurnstileMachine>(
                key,
                nameof(TurnstileTrigger.Coin),
                new JsonObject { ["coin"] = "penny" }
            )
        )
            .Should()
            .BeOfType<AdvanceOutcome.Rejected>("host code's input is validated like a client's");
        (
            await instances.Advance<RuledSystemTurnstileMachine>(
                key,
                nameof(TurnstileTrigger.Coin),
                new JsonObject { ["coin"] = "quarter" }
            )
        )
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>(
                $"host code on a system-owned instance is not checked against event rules. See {Adr}"
            );
    }

    [Test]
    public async Task Host_code_on_a_users_draft_is_a_client_holding_no_roles()
    {
        var owner = NewUser();
        var id = await DeskIn(owner, DeskState.Open);

        AdvanceOutcome outcome;
        using (var scope = _host.Scope())
            outcome = await _host
                .Service(scope, RuledDeskMachine.Id)
                .Advance(owner, id, nameof(DeskTrigger.OperatorOnly), null, "host-1");

        outcome
            .Should()
            .BeOfType<AdvanceOutcome.Rejected>()
            .Which.Reason.Should()
            .Be(
                DraftRefusalCodes.Forbidden,
                $"there is no trusted path to a user's instance: host code there is a client. See {Adr}"
            );
        (await StateOf(id)).Should().Be(nameof(DeskState.Open));
    }

    [Test]
    public Task Forbidden_iff_the_rule_is_unsatisfied() =>
        Gen.Select(Gen.Int[0, 15], Gen.Int[0, EventRules.Length - 1])
            .SampleAsync(
                async Task (int mask, int ruleIndex) =>
                {
                    string[] pool = [DeskRoles.Operator, DeskRoles.Admin, "operator", "Viewer"];
                    var roles = pool.Where((_, bit) => (mask & (1 << bit)) != 0).ToArray();
                    var caller = new Caller($"roles {mask}", roles);
                    var rule = EventRules[ruleIndex];
                    var allowed = Satisfies(caller, rule);

                    var owner = NewUser();
                    var id = await DeskIn(owner, DeskState.Open);
                    var before = (await _host.Rows(id)).Single().ConcurrencyToken;

                    AdvanceOutcome outcome;
                    using (var scope = _host.Scope())
                        outcome = await _host
                            .Service(scope, RuledDeskMachine.Id)
                            .Advance(DeskRoles.Client(owner, roles), id, rule.ToString());

                    var row = (await _host.Rows(id)).Single();
                    var writes = row.ConcurrencyToken == before ? 0 : 1;
                    (outcome is AdvanceOutcome.Rejected { Reason: DraftRefusalCodes.Forbidden })
                        .Should()
                        .Be(!allowed, $"forbidden exactly when the rule is unmet. See {Adr}");
                    writes.Should().Be(allowed ? 1 : 0, "a forbidden event writes nothing");
                },
                iter: 32,
                threads: 1,
                print: t => $"roles mask {t.Item1}, rule {EventRules[t.Item2]}"
            );

    [Test]
    public async Task A_replay_by_a_caller_who_lost_the_role_is_forbidden_not_replayed()
    {
        var owner = NewUser();
        var id = await DeskIn(owner, DeskState.Open);

        using var scope = _host.Scope();
        var service = _host.Service(scope, RuledDeskMachine.Id);
        (
            await service.Advance(
                DeskRoles.Client(owner, DeskRoles.Operator),
                id,
                nameof(DeskTrigger.OperatorOnly),
                requestId: "r1"
            )
        )
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>();

        (
            await service.Advance(
                DeskRoles.Client(owner),
                id,
                nameof(DeskTrigger.OperatorOnly),
                requestId: "r1"
            )
        )
            .Should()
            .BeOfType<AdvanceOutcome.Rejected>()
            .Which.Reason.Should()
            .Be(
                DraftRefusalCodes.Forbidden,
                $"the rule is checked before a request id is replayed. See {Adr}"
            );
    }

    [Test]
    public async Task Roles_are_read_per_event_not_cached_on_the_draft()
    {
        var owner = NewUser();
        var id = await DeskIn(owner, DeskState.Open);

        using var scope = _host.Scope();
        var service = _host.Service(scope, RuledDeskMachine.Id);
        (
            await service.Advance(
                DeskRoles.Client(owner, DeskRoles.Operator),
                id,
                nameof(DeskTrigger.OperatorOnly),
                requestId: "as-operator"
            )
        )
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>();

        // The same user, the next event, without the role now.
        (
            await service.Advance(
                DeskRoles.Client(owner),
                id,
                nameof(DeskTrigger.Reopen),
                requestId: "role-revoked"
            )
        )
            .Should()
            .BeOfType<AdvanceOutcome.Rejected>()
            .Which.Reason.Should()
            .Be(
                DraftRefusalCodes.Forbidden,
                $"a role once held is not remembered on the draft. See {Adr}"
            );
        (await StateOf(id)).Should().Be(nameof(DeskState.Done));

        (
            await service.Advance(
                DeskRoles.Client(owner, DeskRoles.Operator),
                id,
                nameof(DeskTrigger.Reopen),
                requestId: "role-restored"
            )
        )
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>();
        (await StateOf(id)).Should().Be(nameof(DeskState.Open));
    }

    [Test]
    public async Task Start_requires_its_start_rule()
    {
        var owner = NewUser();
        using var scope = _host.Scope();
        var service = _host.Service(scope, RuledDeskMachine.Id);

        (
            await service.Start(
                DeskRoles.Client(owner),
                new JsonObject { ["title"] = "mine" },
                "start-1"
            )
        )
            .Should()
            .BeOfType<StartOutcome.Rejected>()
            .Which.Code.Should()
            .Be(DraftRefusalCodes.Forbidden, $"start declares its rule like an event. See {Adr}");
        (await _host.UserRows(owner, RuledDeskMachine.Id))
            .Should()
            .BeEmpty("a forbidden start writes nothing");

        (
            await service.Start(
                DeskRoles.Client(owner, DeskRoles.Operator),
                new JsonObject { ["title"] = "mine" },
                "start-2"
            )
        )
            .Should()
            .BeOfType<StartOutcome.Started>();
        (await _host.UserRows(owner, RuledDeskMachine.Id)).Should().ContainSingle();
    }

    /// <summary>
    /// A launcher whose authorization refuses every run, as the mediator's does for a user without the train's
    /// roles, and counts how often it was asked.
    /// </summary>
    private sealed class AuthorizingLauncher : IInvokedTrainLauncher
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public IReadOnlyList<string> Refusals(
            InvokedTrainDeclaration declaration,
            IServiceProvider services
        ) => [];

        public Task Launch(
            InvokedTrainLaunch launch,
            IDataContext context,
            CancellationToken cancellationToken = default
        )
        {
            Interlocked.Increment(ref _calls);
            throw new UnauthorizedAccessException("The caller may not run this train.");
        }
    }
}
