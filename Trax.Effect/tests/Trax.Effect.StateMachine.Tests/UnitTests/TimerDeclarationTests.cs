using System.Reflection;
using System.Text.Json.Nodes;
using System.Xml;
using AwesomeAssertions;
using Ingest.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Attributes;
using Trax.Effect.StateMachine.Persistence;
using Trax.Effect.StateMachine.Tests.Fakes;

namespace Trax.Effect.StateMachine.Tests.UnitTests;

/// <summary>
/// A state's timer, the pure half: what the builder accepts and refuses, how the timer and the states around it
/// are classified, how a duration read from the context is checked when the state is entered, and that only
/// <c>AdvanceTimer</c> fires a timer.
/// </summary>
[Property("adr", TriggerInputValidationTests.Adr)]
public class TimerDeclarationTests
{
    private const string Adr = TriggerInputValidationTests.Adr;

    private static readonly TimeSpan OneMinute = TimeSpan.FromMinutes(1);

    public enum TimedState
    {
        Idle,
        Review,
        Expired,
        Done,
        Fetching,
        Fetched,
        FetchFailed,
        Cancelled,
    }

    public enum TimedTrigger
    {
        Open,
        Reopen,
        Approve,
        Extend,
        Withdraw,
    }

    // The reminder's shape with the variants' extra triggers: Idle -(Open)-> Review, Review -(Approve)-> Done.
    private static MachineBuilder<TimedState, TimedTrigger> Reminder()
    {
        var m = new MachineBuilder<TimedState, TimedTrigger>();
        m.Id("timed").Version(1).StartsAt(TimedState.Idle, () => new JsonObject());
        m.In(TimedState.Idle).Context().On(TimedTrigger.Open).To(TimedState.Review);
        m.In(TimedState.Review).Context().On(TimedTrigger.Approve).To(TimedState.Done);
        m.In(TimedState.Expired).Context();
        m.In(TimedState.Done).Context();
        return m;
    }

    private static Snapshot At(TimedState state) =>
        new()
        {
            Machine = "timed",
            Version = 1,
            State = state.ToString(),
            Context = new JsonObject(),
        };

    private static void RefusedAtBuild(MachineBuilder<TimedState, TimedTrigger> m, string message)
    {
        var build = () => m.Build();

        build
            .Should()
            .Throw<InvalidOperationException>(
                $"the machine's timer is refused when it is built ({Adr})"
            )
            .WithMessage(message);
    }

    // Fetching invokes the fetch train, its outcomes going to Fetched, FetchFailed and Cancelled.
    private static void InvokingFetching(IMachineBuilder<TimedState, TimedTrigger> m) =>
        m.In(TimedState.Fetching)
            .Context()
            .Invokes<IFetchTrain, FetchInput, FetchOutput>(_ => new FetchInput("s"))
            .OnDone(TimedState.Fetched)
            .OnFailed(TimedState.FetchFailed)
            .OnCancelled(TimedState.Cancelled);

    // ---- Declaration ----

    [Test]
    public void A_second_After_on_one_state_is_refused_at_build()
    {
        var m = Reminder();
        m.In(TimedState.Review)
            .After(TimeSpan.FromMinutes(10))
            .GoTo(TimedState.Expired)
            .After(TimeSpan.FromMinutes(20))
            .GoTo(TimedState.Done);

        RefusedAtBuild(m, "*Review declares two timers*");
    }

    [Test]
    public void A_self_loop_timer_is_refused_at_build()
    {
        var m = Reminder();
        m.In(TimedState.Review).After(OneMinute).GoTo(TimedState.Review);

        RefusedAtBuild(m, "*Review*");
    }

    [Test]
    public void A_timer_only_cycle_is_refused_at_build()
    {
        var cycle = Reminder();
        cycle.In(TimedState.Review).After(OneMinute).GoTo(TimedState.Expired);
        cycle.In(TimedState.Expired).After(OneMinute).GoTo(TimedState.Review);

        RefusedAtBuild(cycle, "*Review*Expired*");

        // One user edge in the cycle breaks it: someone has to act for the loop to go round.
        var broken = Reminder();
        broken.In(TimedState.Review).After(OneMinute).GoTo(TimedState.Expired);
        broken.In(TimedState.Expired).On(TimedTrigger.Reopen).To(TimedState.Review);

        broken.Invoking(b => b.Build()).Should().NotThrow();
    }

    [Test]
    public void A_timer_into_an_effects_target_is_refused_at_build()
    {
        var m = new MachineBuilder<TimedState, TimedTrigger>();
        m.Id("timed").StartsAt(TimedState.Idle, () => new JsonObject());
        m.In(TimedState.Idle).Context().On(TimedTrigger.Open).To(TimedState.Review);
        m.In(TimedState.Review)
            .Context()
            .After(OneMinute)
            .GoTo(TimedState.Done)
            .On(TimedTrigger.Approve)
            .RunsOnce<object>()
            .To(TimedState.Done);
        m.In(TimedState.Done).Context();

        RefusedAtBuild(m, "*Done*target of the effect*");
    }

    // Green today, and it stays so: a timer carries the context unchanged, so its target is not reserved.
    [Test]
    public void A_timer_target_is_not_reserved()
    {
        ReminderMachine
            .Built.ReservedStates.Should()
            .NotContain(
                ReminderState.Expired,
                $"a timer's target holds nothing only the server could have produced ({Adr})"
            );
    }

    [Test]
    public void The_timed_state_is_locked()
    {
        var service = ReminderMachine.AsMachine().CreateService(null!, null);

        // The locked set is private to the draft service: it is what autosave may not leave or rewrite.
        var locked =
            (IReadOnlySet<string>)
                service
                    .GetType()
                    .GetProperty("LockedNames", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .GetValue(service)!;

        locked
            .Should()
            .Contain(
                nameof(ReminderState.Review),
                $"autosave may not leave or rewrite a timed state, or the timer would fire on stale data ({Adr})"
            );
    }

    // Green today, and it stays so.
    [Test]
    public void A_user_edge_into_a_timer_target_is_allowed()
    {
        var m = Reminder();
        m.In(TimedState.Review)
            .After(OneMinute)
            .GoTo(TimedState.Expired)
            .On(TimedTrigger.Withdraw)
            .To(TimedState.Expired);

        m.Invoking(b => b.Build()).Should().NotThrow();
    }

    [Test]
    public void A_timer_into_an_invoking_state_on_a_user_owned_machine_refuses_startup()
    {
        var machine = new ConfiguredMachine<TimedState, TimedTrigger>(m =>
        {
            m.Id("timed").StartsAt(TimedState.Idle, () => new JsonObject());
            m.In(TimedState.Idle).Context().After(OneMinute).GoTo(TimedState.Fetching);
            InvokingFetching(m);
        });

        var problems = InvokesStartupValidator.Problems(
            machine,
            new ServiceCollection().BuildServiceProvider()
        );

        problems
            .Should()
            .Contain(
                p => p.Contains("timer") && p.Contains("Fetching"),
                $"a timer would queue the run with nobody present to authorize it ({Adr})"
            );
    }

    // Green today, and it stays so.
    [Test]
    public void A_timer_into_an_invoking_state_on_a_system_owned_machine_is_allowed()
    {
        var machine = new ConfiguredMachine<TimedState, TimedTrigger>(m =>
        {
            m.Id("timed").SystemOwned().StartsAt(TimedState.Idle, () => new JsonObject());
            m.In(TimedState.Idle).Context().After(OneMinute).GoTo(TimedState.Fetching);
            InvokingFetching(m);
        });

        var problems = InvokesStartupValidator.Problems(
            machine,
            new ServiceCollection().BuildServiceProvider()
        );

        problems.Should().NotContain(p => p.Contains("timer"));
    }

    // Green today, and it stays so.
    [Test]
    public void A_timer_leaving_an_invoking_state_is_allowed()
    {
        var m = new MachineBuilder<WaitState, WaitTrigger>();
        WaitMachine.Configure(m);

        m.Invoking(b => b.Build()).Should().NotThrow();
    }

    [Test]
    public void After_on_a_committed_state_leaves_it()
    {
        var m = Reminder();
        m.In(TimedState.Done).Committed().After(OneMinute).GoTo(TimedState.Expired);
        var built = m.Build();

        built
            .Engine.AdvanceTimer(At(TimedState.Done), "Done.after")
            .Should()
            .BeOfType<AdvanceResult.Transitioned>(
                $"a timer is the server applying a declared edge, so it may leave a committed state ({Adr})"
            )
            .Which.Snapshot.State.Should()
            .Be(nameof(TimedState.Expired));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void A_zero_or_negative_fixed_duration_is_refused_at_build(int seconds)
    {
        var m = Reminder();
        m.In(TimedState.Review).After(TimeSpan.FromSeconds(seconds)).GoTo(TimedState.Expired);

        RefusedAtBuild(m, "*Review*");
    }

    [Test]
    public void A_fixed_duration_above_the_global_cap_is_refused_at_build()
    {
        var m = Reminder();
        m.In(TimedState.Review).After(TimeSpan.MaxValue).GoTo(TimedState.Expired);

        RefusedAtBuild(m, "*Review*");
    }

    [TestCase(10, 1)]
    [TestCase(0, 60)]
    [TestCase(-1, 60)]
    public void A_context_duration_with_min_above_max_or_min_not_positive_is_refused_at_build(
        int minMinutes,
        int maxMinutes
    )
    {
        var m = new MachineBuilder<WaitState, WaitTrigger>();
        m.Id("wait").StartsAt(WaitState.Idle, () => new JsonObject());
        m.In(WaitState.Idle)
            .Context<WaitMachine.WaitContext>()
            .After<WaitMachine.WaitContext>(
                c => c.Patience,
                TimeSpan.FromMinutes(minMinutes),
                TimeSpan.FromMinutes(maxMinutes)
            )
            .GoTo(WaitState.TimedOut);
        m.In(WaitState.TimedOut).Context<WaitMachine.WaitContext>();

        var build = () => m.Build();

        build.Should().Throw<InvalidOperationException>().WithMessage("*Idle*");
    }

    [Test]
    public void A_duration_field_not_on_the_states_context_is_refused_at_build()
    {
        var m = new MachineBuilder<WaitState, WaitTrigger>();
        m.Id("wait").StartsAt(WaitState.Idle, () => new JsonObject());
        m.In(WaitState.Idle)
            .Context<WaitMachine.WaitContext>()
            .After<OtherContext>(c => c.Missing, OneMinute, TimeSpan.FromHours(1))
            .GoTo(WaitState.TimedOut);
        m.In(WaitState.TimedOut).Context<WaitMachine.WaitContext>();

        var build = () => m.Build();

        build.Should().Throw<InvalidOperationException>().WithMessage("*missing*");
    }

    [Test]
    public void A_duration_field_of_the_wrong_type_is_refused_at_build()
    {
        // The state's context declares patience as an int; the timer reads it as a duration.
        var m = new MachineBuilder<WaitState, WaitTrigger>();
        m.Id("wait").StartsAt(WaitState.Idle, () => new JsonObject { ["patience"] = 300 });
        m.In(WaitState.Idle)
            .Context<IntPatienceContext>()
            .After<WaitMachine.WaitContext>(c => c.Patience, OneMinute, TimeSpan.FromHours(1))
            .GoTo(WaitState.TimedOut);
        m.In(WaitState.TimedOut).Context<IntPatienceContext>();

        var build = () => m.Build();

        build.Should().Throw<InvalidOperationException>().WithMessage("*patience*");
    }

    [Test]
    public void A_delegate_duration_is_refused_export()
    {
        var m = new MachineBuilder<WaitState, WaitTrigger>();
        m.Id("wait").StartsAt(WaitState.Idle, () => new JsonObject());
        m.In(WaitState.Idle)
            .Context<WaitMachine.WaitContext>()
            .After<WaitMachine.WaitContext>(
                c => c.Patience ?? TimeSpan.FromMinutes(5),
                OneMinute,
                TimeSpan.FromHours(1)
            )
            .GoTo(WaitState.TimedOut);
        m.In(WaitState.TimedOut).Context<WaitMachine.WaitContext>();
        var built = m.Build();

        var export = () => IrExporter.Export(built);

        export
            .Should()
            .Throw<InvalidOperationException>(
                $"a computed duration has no exportable data for the twin to show ({Adr})"
            );
    }

    [Test]
    public void A_duration_field_marked_TraxSensitive_is_refused_at_build()
    {
        var m = new MachineBuilder<WaitState, WaitTrigger>();
        m.Id("wait").StartsAt(WaitState.Idle, () => new JsonObject());
        m.In(WaitState.Idle)
            .Context<SensitiveContext>()
            .After<SensitiveContext>(c => c.Patience, OneMinute, TimeSpan.FromHours(1))
            .GoTo(WaitState.TimedOut);
        m.In(WaitState.TimedOut).Context<SensitiveContext>();

        var build = () => m.Build();

        build
            .Should()
            .Throw<InvalidOperationException>(
                $"a deadline is exported and shown, so it cannot come from a sensitive field ({Adr})"
            )
            .WithMessage("*patience*");
    }

    [Test]
    public void A_timer_edge_carries_the_union_of_the_rules_entering_its_source()
    {
        IMachine machine = new ConfiguredMachine<TimedState, TimedTrigger>(m =>
        {
            m.Id("timed").StartsAt(TimedState.Idle, () => new JsonObject());
            m.In(TimedState.Idle)
                .Context()
                .On(TimedTrigger.Open)
                .Requires("Editor")
                .To(TimedState.Review);
            m.In(TimedState.Done)
                .Context()
                .On(TimedTrigger.Reopen)
                .Requires("Reviewer")
                .To(TimedState.Review);
            m.In(TimedState.Review)
                .Context()
                .After(OneMinute)
                .GoTo(TimedState.Expired)
                .On(TimedTrigger.Approve)
                .To(TimedState.Done);
            m.In(TimedState.Expired).Context();
        });

        machine
            .Triggers.Should()
            .ContainSingle(t => t.Name == "Review.after")
            .Which.Kind.Should()
            .Be(TriggerKind.Timer);
        var timer = MachineCensus
            .Describe([machine])
            .Should()
            .ContainSingle(e => e.Name == "Review.after")
            .Subject;
        timer.Kind.Should().Be(nameof(TriggerKind.Timer));
        timer
            .Rule.Should()
            .ContainAll(
                ["Editor", "Reviewer"],
                $"a timer acts for whoever could have entered its state ({Adr})"
            );
    }

    [Test]
    public void The_reminder_IR_matches_the_committed_golden_the_twin_reads()
    {
        var file = Helpers.FixturePaths.IrFile("reminder");
        file.Should().NotBeNull("the shared machines/ directory is part of the repository");
        var exported = IrExporter.Export(ReminderMachine.Built);

        if (Environment.GetEnvironmentVariable("UPDATE_IR") == "1")
            File.WriteAllText(file!, exported + "\n");

        File.ReadAllText(file!)
            .TrimEnd('\n')
            .Should()
            .Be(
                exported,
                "the twin and its corpus are generated from the C# machine's IR; regenerate the golden "
                    + "deliberately with UPDATE_IR=1, then the twin and the corpus from it"
            );
    }

    // ---- Context durations at entry ----

    private static AdvanceResult EnterFetching(JsonNode? patience) =>
        WaitMachine.Machine.Advance(WaitMachine.IdleWith(patience), nameof(WaitTrigger.Start));

    private static void ShouldBeInvalidInput(AdvanceResult result) =>
        result
            .Should()
            .BeOfType<AdvanceResult.Rejected>()
            .Which.Reason.Should()
            .Be(
                RefusalCode.InvalidInput,
                $"a duration outside its declared bounds cannot arm the timer ({Adr})"
            );

    [Test]
    public void Entering_a_timed_state_with_duration_null() =>
        ShouldBeInvalidInput(EnterFetching(null));

    [Test]
    public void Entering_a_timed_state_with_duration_zero() =>
        ShouldBeInvalidInput(EnterFetching("PT0S"));

    [Test]
    public void Entering_a_timed_state_with_duration_negative() =>
        ShouldBeInvalidInput(EnterFetching("-PT5M"));

    [Test]
    public void Entering_a_timed_state_with_duration_below_min() =>
        ShouldBeInvalidInput(EnterFetching("PT30S"));

    [Test]
    public void Entering_a_timed_state_with_duration_above_max() =>
        ShouldBeInvalidInput(EnterFetching("PT2H"));

    [Test]
    public void Entering_a_timed_state_with_duration_max_value()
    {
        AdvanceResult result = null!;
        var enter = () => result = EnterFetching(XmlConvert.ToString(TimeSpan.MaxValue));

        enter.Should().NotThrow("an overflowing duration is an answer, never an exception");
        ShouldBeInvalidInput(result);
    }

    // Green today, and it stays so.
    [Test]
    public void Entering_a_timed_state_with_duration_in_range() =>
        EnterFetching("PT5M")
            .Should()
            .BeOfType<AdvanceResult.Transitioned>()
            .Which.Snapshot.State.Should()
            .Be(nameof(WaitState.Fetching));

    [Test]
    public void Entering_a_timed_state_with_duration_in_prose() =>
        ShouldBeInvalidInput(EnterFetching("5 minutes"));

    [Test]
    public void Entering_a_timed_state_with_duration_as_a_number() =>
        ShouldBeInvalidInput(EnterFetching(300));

    // .NET's \d matches every Unicode decimal digit; the duration grammar is ASCII, as the twin's is.
    [TestCase("PT\u0665M")]
    [TestCase("PT\uFF15M")]
    [TestCase("P\u0661DT5M")]
    public void Entering_a_timed_state_with_a_non_ascii_digit_in_the_duration(string patience)
    {
        AdvanceResult result = null!;
        var enter = () => result = EnterFetching(patience);

        enter
            .Should()
            .NotThrow("a duration the grammar does not hold is an answer, never an exception");
        ShouldBeInvalidInput(result);
    }

    [TestCase("PT\u0665M")]
    [TestCase("PT\uFF15M")]
    public void The_timer_of_a_row_with_a_non_ascii_duration_is_a_problem_not_a_throw(
        string patience
    )
    {
        (SnapshotTimer? Timer, string? Problem) answer = default;
        var read = () =>
            answer = WaitMachine.Machine.TimerFor(
                nameof(WaitState.Fetching),
                WaitMachine.IdleWith(patience).Context,
                restart: true
            );

        read.Should().NotThrow("reading a row's timer never throws on what the context holds");
        answer.Timer.Should().BeNull();
        answer.Problem.Should().NotBeNull();
    }

    // ---- One engine contract ----

    [Test]
    public void Advance_never_applies_a_timer_trigger()
    {
        var review = ReminderMachine.At(ReminderState.Review);

        ReminderMachine
            .Machine.Advance(review, "Review.after")
            .Should()
            .BeOfType<AdvanceResult.Rejected>()
            .Which.Reason.Should()
            .Be(RefusalCode.TimerBound, $"only the server fires a timer ({Adr})");
        ReminderMachine.Machine.CanFire(review, "Review.after").Should().BeFalse();
    }

    [Test]
    public void AdvanceTimer_moves_only_from_its_state()
    {
        ReminderMachine
            .Machine.AdvanceTimer(ReminderMachine.At(ReminderState.Idle), "Review.after")
            .Should()
            .BeOfType<AdvanceResult.Rejected>()
            .Which.Reason.Should()
            .Be(RefusalCode.NoTransition);

        ReminderMachine
            .Machine.AdvanceTimer(ReminderMachine.At(ReminderState.Review), "Review.after")
            .Should()
            .BeOfType<AdvanceResult.Transitioned>(
                $"the timer of the state the snapshot is in moves it to the timer's target ({Adr})"
            )
            .Which.Snapshot.State.Should()
            .Be(nameof(ReminderState.Expired));

        // The context is carried to the target.
        var fetching = WaitMachine.IdleWith("PT5M") with
        {
            State = nameof(WaitState.Fetching),
        };
        var timedOut = WaitMachine
            .Machine.AdvanceTimer(fetching, "Fetching.after")
            .Should()
            .BeOfType<AdvanceResult.Transitioned>()
            .Subject.Snapshot;
        timedOut.State.Should().Be(nameof(WaitState.TimedOut));
        JsonNode.DeepEquals(timedOut.Context, fetching.Context).Should().BeTrue();
    }

    public sealed record OtherContext
    {
        public TimeSpan? Missing { get; init; }
    }

    public sealed record IntPatienceContext
    {
        public int Patience { get; init; }
    }

    public sealed record SensitiveContext
    {
        [TraxSensitive]
        public TimeSpan? Patience { get; init; }
    }
}
