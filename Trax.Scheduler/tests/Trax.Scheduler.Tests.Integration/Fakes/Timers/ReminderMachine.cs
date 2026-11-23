using System.Text.Json.Nodes;
using Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Scheduler.Tests.Integration.Fakes.Timers;

/// <summary>
/// A user's reminder: <c>Idle</c> --Open--> <c>Review</c>, which expires into <c>Expired</c> after
/// <see cref="Delay"/>. From <c>Review</c>, <c>Approve</c> enters <c>Done</c>, <c>Extend</c> enters <c>Review</c>
/// again (restarting its timer) and <c>Publish</c> runs the machine's one effect, <see cref="IReminderPublish"/>,
/// into <c>Published</c>. <c>Expired</c> holds no context marked <c>blocked</c>, so a test can make a fire
/// unappliable by marking it.
/// </summary>
/// <remarks>
/// The assembly scan registers the parameterless version 1 on every host; a test deploys another
/// <see cref="ReminderShape"/> by replacing that registration (see <see cref="TimerHosts.Deploy"/>).
/// </remarks>
public sealed class ReminderMachine : Machine<ReminderState, ReminderTrigger>
{
    public const string MachineId = "timer-reminder";

    public const string EffectKey = "timer-reminder:publish";

    public static readonly TimeSpan Delay = TimeSpan.FromMinutes(10);

    public static readonly TimeSpan LongerDelay = TimeSpan.FromHours(1);

    private readonly ReminderShape _shape;

    public ReminderMachine()
        : this(ReminderShape.V1) { }

    public ReminderMachine(ReminderShape shape) => _shape = shape;

    public ReminderShape Shape => _shape;

    protected override void Configure(IMachineBuilder<ReminderState, ReminderTrigger> m)
    {
        m.Id(MachineId)
            .Version(_shape == ReminderShape.V1 ? 1 : 2)
            .StartsAt(ReminderState.Idle, () => Context());
        if (_shape != ReminderShape.V1)
            m.MigrateFrom(1, (state, context) => new MigrationResult(state, context));

        m.Discard(r => r.OwnerOnly());

        // Open is ungated: a gated event's target is the record of the server's decision, kept past the draft
        // lifetime and never discarded, and these tests bound, discard and re-create reviews.
        m.In(ReminderState.Idle)
            .On(ReminderTrigger.Open)
            .AnyAuthenticated()
            .To(ReminderState.Review);

        var review = m.In(ReminderState.Review);
        review = _shape switch
        {
            ReminderShape.V2WithoutTimer => review,
            ReminderShape.V2LongerTimer => review.After(LongerDelay).GoTo(ReminderState.Expired),
            _ => review.After(Delay).GoTo(ReminderState.Expired),
        };
        review
            .On(ReminderTrigger.Approve)
            .AnyAuthenticated()
            .To(ReminderState.Done)
            .On(ReminderTrigger.Extend)
            .AnyAuthenticated()
            .To(ReminderState.Review)
            .On(ReminderTrigger.Publish)
            .AnyAuthenticated()
            .RunsOnce<IReminderPublish>(EffectKey)
            .To(ReminderState.Published);

        m.In(ReminderState.Expired)
            .Holds(ctx => ctx["blocked"] is null ? null : "An expired reminder is never blocked.");
    }

    /// <summary>The context a reminder starts with.</summary>
    public static JsonObject Context(string note = "") => new() { ["note"] = note };

    /// <summary>A snapshot of the reminder in <paramref name="state"/> as a client sends it.</summary>
    public static string Json(string state, JsonObject? context = null, int version = 1) =>
        new JsonObject
        {
            ["machine"] = MachineId,
            ["version"] = version,
            ["state"] = state,
            ["context"] = context ?? Context(),
        }.ToJsonString();
}
