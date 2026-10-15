using System.Reflection;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.Effect.Attributes;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;
using Trax.Effect.StateMachine.Persistence.Mutations;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// A machine a client can reach declares a rule for every event, for its <c>start</c> and for its reads, and a host
/// that serves one with a rule missing, impossible or too loose refuses to start, naming the machine and what is
/// missing. A machine nothing serves needs no rules and gets no surface.
/// </summary>
[Property("adr", Adr)]
public class EventRuleStartupTests
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    [Test]
    public async Task An_undeclared_client_event_refuses_startup()
    {
        var refusal = await StartingHost.Start(_ => { }, Marked.Machine<PartlyRuledMachine>());

        refusal
            .Should()
            .BeOfType<InvalidOperationException>(
                $"a marked machine with an event no rule guards must not be served. See {Adr}"
            )
            .Which.Message.Should()
            .Contain(PartlyRuledMachine.Id)
            .And.Contain(nameof(TurnstileTrigger.Push));
    }

    [Test]
    public async Task Every_undeclared_event_is_listed_in_one_refusal()
    {
        var refusal = await StartingHost.Start(
            _ => { },
            Marked.Machine<DeclarativeTurnstileMachine>()
        );

        refusal
            .Should()
            .BeOfType<InvalidOperationException>(
                $"one refusal lists every gap, so a host is fixed in one pass. See {Adr}"
            )
            .Which.Message.Should()
            .Contain("declarative-turnstile")
            .And.Contain(nameof(TurnstileTrigger.Coin))
            .And.Contain(nameof(TurnstileTrigger.Push));
    }

    [Test]
    public void Two_edges_of_one_event_declaring_different_rules_refuse_to_build()
    {
        var m = new MachineBuilder<DeskState, DeskTrigger>();
        m.Id("two-rules").Version(1).StartsAt(DeskState.Draft, () => new JsonObject());
        m.In(DeskState.Draft).On(DeskTrigger.Open).AnyAuthenticated().To(DeskState.Open);
        m.In(DeskState.Open).On(DeskTrigger.Reopen).Requires(DeskRoles.Operator).To(DeskState.Done);
        m.In(DeskState.Done).On(DeskTrigger.Reopen).AnyAuthenticated().To(DeskState.Open);

        var build = () => m.Build();

        build
            .Should()
            .Throw<InvalidOperationException>(
                $"an event has one rule, whichever state it is sent from. See {Adr}"
            )
            .Which.Message.Should()
            .Contain(nameof(DeskTrigger.Reopen));
    }

    [Test]
    public void A_rule_on_an_outcome_or_timer_trigger_refuses_to_build()
    {
        // Outcome and timer triggers are server-only, so the builders that declare them offer no caller rule at
        // all: a rule on one cannot be written, which is stronger than refusing it when the machine is built.
        string[] callerRules = ["Requires", "RequiresPolicy", "AnyAuthenticated", "Anonymous"];

        typeof(ITimerBuilder<DeskState, DeskTrigger>)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(method => method.Name)
            .Should()
            .NotContain(
                callerRules,
                $"a timer is fired by the server, never sent by a caller. See {Adr}"
            );

        var outcomes = typeof(IInvokeBuilder<DeskState, DeskTrigger>)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.Name is "OnDone" or "OnFailed" or "OnCancelled")
            .ToList();
        outcomes.Should().NotBeEmpty();
        outcomes
            .Should()
            .AllSatisfy(method =>
            {
                method
                    .ReturnType.Should()
                    .NotBe(
                        typeof(ITransitionBuilder<DeskState, DeskTrigger>),
                        $"an outcome is applied by the server, never sent by a caller. See {Adr}"
                    );
                method
                    .GetParameters()
                    .Select(parameter => parameter.ParameterType)
                    .Should()
                    .NotContain(typeof(string[]), "an outcome takes no roles");
            });
    }

    [Test]
    public async Task A_policy_rule_naming_an_unregistered_policy_refuses_startup()
    {
        var refusal = await StartingHost.Start(_ => { }, Marked.Machine<PolicyRuledMachine>());

        refusal
            .Should()
            .BeOfType<InvalidOperationException>(
                $"an event no caller could ever pass must not be served. See {Adr}"
            )
            .Which.Message.Should()
            .Contain(PolicyRuledMachine.MissingPolicy)
            .And.Contain(PolicyRuledMachine.Id)
            .And.Contain(nameof(TurnstileTrigger.Coin));
    }

    [Test]
    public async Task Anonymous_on_a_user_owned_machine_refuses_startup()
    {
        var refusal = await StartingHost.Start(_ => { }, Marked.Machine<AnonymousUserMachine>());

        refusal
            .Should()
            .BeOfType<InvalidOperationException>(
                $"a user-owned instance has no owner for an anonymous caller to be. See {Adr}"
            )
            .Which.Message.Should()
            .Contain(AnonymousUserMachine.Id)
            .And.Contain("Anonymous");
    }

    [Test]
    public async Task An_undeclared_start_get_or_push_rule_refuses_startup()
    {
        var refusal = await StartingHost.Start(_ => { }, Marked.Machine<UndeclaredReadsMachine>());

        refusal
            .Should()
            .BeOfType<InvalidOperationException>(
                $"reads and start declare their rule like events do. See {Adr}"
            )
            .Which.Message.Should()
            .Contain(UndeclaredReadsMachine.Id)
            .And.Contain("start")
            .And.Contain("get")
            .And.Contain("changed");
    }

    [Test]
    public async Task An_undeclared_discard_rule_refuses_startup()
    {
        var refusal = await StartingHost.Start(
            _ => { },
            Marked.Machine<UndeclaredDiscardMachine>()
        );

        refusal
            .Should()
            .BeOfType<InvalidOperationException>(
                $"discard declares its rule as start and the reads do. See {Adr}"
            )
            .Which.Message.Should()
            .Contain(UndeclaredDiscardMachine.Id)
            .And.Contain("discard");
    }

    [Test]
    public async Task A_discard_rule_looser_than_owner_only_refuses_startup()
    {
        var refusal = await StartingHost.Start(_ => { }, Marked.Machine<LooseDiscardMachine>());

        refusal
            .Should()
            .BeOfType<InvalidOperationException>(
                $"owner-only is the floor for discarding an instance. See {Adr}"
            )
            .Which.Message.Should()
            .Contain(LooseDiscardMachine.Id)
            .And.Contain("discard");
    }

    [Test]
    public async Task A_read_rule_looser_than_owner_only_refuses_startup()
    {
        var refusal = await StartingHost.Start(_ => { }, Marked.Machine<LooseReadMachine>());

        refusal
            .Should()
            .BeOfType<InvalidOperationException>(
                $"owner-only is the floor for reading an instance. See {Adr}"
            )
            .Which.Message.Should()
            .Contain(LooseReadMachine.Id)
            .And.Contain("get");
    }

    [Test]
    public async Task A_watch_rule_looser_than_get_is_refused_at_startup()
    {
        var refusal = await StartingHost.Start(_ => { }, Marked.Machine<LooseWatchMachine>());

        refusal
            .Should()
            .BeOfType<InvalidOperationException>(
                $"the push carries the whole instance, so it may admit no caller get refuses. See {Adr}"
            )
            .Which.Message.Should()
            .Contain(LooseWatchMachine.Id)
            .And.Contain("changed rule")
            .And.Contain(ViewerReadMachine.Viewer);
    }

    [Test]
    public async Task A_watch_rule_stricter_than_get_starts()
    {
        var refusal = await StartingHost.Start(_ => { }, Marked.Machine<StrictWatchMachine>());

        refusal
            .Should()
            .BeNull($"a push no wider than get streams nothing get withholds. See {Adr}");
    }

    [Test]
    public void A_gated_event_into_the_start_state_refuses_to_build()
    {
        var m = new MachineBuilder<DeskState, DeskTrigger>();
        m.Id("gated-into-start").Version(1).StartsAt(DeskState.Draft, () => new JsonObject());
        m.In(DeskState.Draft).On(DeskTrigger.Open).AnyAuthenticated().To(DeskState.Open);
        m.In(DeskState.Open)
            .On(DeskTrigger.Reopen)
            .Requires(DeskRoles.Operator)
            .To(DeskState.Draft);

        var build = () => m.Build();

        build
            .Should()
            .Throw<InvalidOperationException>(
                $"anyone may start an instance, so a gated event into the start state gates nothing. See {Adr}"
            )
            .Which.Message.Should()
            .Contain(nameof(DeskTrigger.Reopen))
            .And.Contain(nameof(DeskState.Draft));
    }

    [Test]
    public void A_policy_gated_event_into_the_start_state_refuses_to_build()
    {
        var m = new MachineBuilder<DeskState, DeskTrigger>();
        m.Id("policy-into-start").Version(1).StartsAt(DeskState.Draft, () => new JsonObject());
        m.In(DeskState.Draft).On(DeskTrigger.Open).AnyAuthenticated().To(DeskState.Open);
        m.In(DeskState.Open)
            .On(DeskTrigger.Reopen)
            .RequiresPolicy(DeskRoles.Policy)
            .To(DeskState.Draft);

        var build = () => m.Build();

        build
            .Should()
            .Throw<InvalidOperationException>(
                $"a policy gates an event as a role does, and anyone may start an instance. See {Adr}"
            )
            .Which.Message.Should()
            .Contain(nameof(DeskTrigger.Reopen))
            .And.Contain(nameof(DeskState.Draft));
    }

    [Test]
    public void A_rule_naming_a_role_or_a_policy_is_gated_and_a_start_rule_counts()
    {
        CallerRule Rule(Action<CallerRuleDraft> declare)
        {
            var draft = new CallerRuleDraft("test", read: false);
            declare(draft);
            return draft.Build()!;
        }

        var role = Rule(d => d.Requires([DeskRoles.Operator]));
        var policy = Rule(d => d.RequiresPolicy(DeskRoles.Policy));
        var open = Rule(d => d.Kind(CallerRuleKind.AnyAuthenticated));

        role.Gated.Should().BeTrue($"a role is a restriction. See {Adr}");
        policy.Gated.Should().BeTrue($"a policy is a restriction as a role is. See {Adr}");
        open.Gated.Should()
            .BeFalse($"AnyAuthenticated restricts nothing beyond signing in. See {Adr}");

        var events = new Dictionary<string, CallerRule>(StringComparer.Ordinal) { ["Open"] = open };
        new MachineRules(events, open, null, null)
            .AnyGated.Should()
            .BeFalse($"nothing beyond signing in is required. See {Adr}");
        new MachineRules(
            new Dictionary<string, CallerRule>(events) { ["Approve"] = policy },
            open,
            null,
            null
        )
            .AnyGated.Should()
            .BeTrue($"a policy-gated event is a restricted event. See {Adr}");
        new MachineRules(events, role, null, null)
            .AnyGated.Should()
            .BeTrue(
                $"a start rule stricter than AnyAuthenticated restricts who creates an instance. See {Adr}"
            );
    }

    [Test]
    public async Task Exposing_a_raw_delegate_or_system_owned_machine_refuses_startup()
    {
        var raw = await StartingHost.Start(_ => { }, Marked.Machine<RuledRawTurnstileMachine>());
        var system = await StartingHost.Start(
            _ => { },
            Marked.Machine<RuledSystemTurnstileMachine>()
        );

        raw.Should()
            .BeOfType<InvalidOperationException>(
                $"a raw-delegate machine has no IR to build a surface from. See {Adr}"
            )
            .Which.Message.Should()
            .Contain(RuledRawTurnstileMachine.Id);
        system
            .Should()
            .BeOfType<InvalidOperationException>(
                $"a system-owned machine has no client surface. See {Adr}"
            )
            .Which.Message.Should()
            .Contain(RuledSystemTurnstileMachine.Id);
    }

    [Test]
    public async Task An_unexposed_machine_with_the_generic_opt_in_off_needs_no_rules_and_has_no_surface()
    {
        var refusal = await StartingHost.Start(_ => { }, new DeclarativeTurnstileMachine());

        refusal
            .Should()
            .BeNull(
                $"a machine nothing serves is reached from code only, so it needs no rules. See {Adr}"
            );
        GenericMutationExposure
            .Exposed()
            .Should()
            .BeEmpty(
                $"without the opt-in no client reaches a machine through the generic mutations. See {Adr}"
            );
    }
}

/// <summary>The generic state-machine trains that carry a GraphQL exposure.</summary>
internal static class GenericMutationExposure
{
    /// <summary>Every generic state-machine train type that declares a <see cref="TraxMutationAttribute"/>.</summary>
    public static IReadOnlyList<Type> Exposed() =>
        new[]
        {
            typeof(SaveSnapshot),
            typeof(AdvanceSnapshot),
            typeof(LoadSnapshot),
            typeof(SendSnapshot),
            typeof(StartSnapshot),
            typeof(DiscardSnapshot),
        }
            .Where(type => type.GetCustomAttribute<TraxMutationAttribute>() is not null)
            .ToList();
}
