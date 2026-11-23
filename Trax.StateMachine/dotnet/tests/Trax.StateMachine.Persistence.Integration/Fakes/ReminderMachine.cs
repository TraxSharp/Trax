using System.Text.Json.Nodes;
using Trax.Effect.Services.ServiceTrain;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>
/// A user's reminder with a timed state: an editor opens it into <c>Review</c>, which expires after
/// <see cref="ReviewFor"/> unless it is approved first or extended (a self-entry that restarts the timer).
/// <c>Expired</c> is timed too, so a timer firing lands in another timed state.
/// </summary>
public sealed class ReminderMachine : Machine<ReminderState, ReminderTrigger>
{
    public const string Id = "reminder";

    /// <summary>The name of the timer trigger of <c>Review</c>.</summary>
    public const string ReviewTimer = "Review.after";

    public static readonly TimeSpan ReviewFor = TimeSpan.FromMinutes(10);

    public static readonly TimeSpan ExpiredFor = TimeSpan.FromDays(1);

    public sealed record ApproveInput
    {
        public string Verdict { get; init; } = "";
    }

    protected override void Configure(IMachineBuilder<ReminderState, ReminderTrigger> m) =>
        Declare(m, Id, gated: true);

    /// <summary>The reminder under <paramref name="id"/>; <paramref name="gated"/> puts a role rule on <c>Open</c>.</summary>
    internal static void Declare(
        IMachineBuilder<ReminderState, ReminderTrigger> m,
        string id,
        bool gated
    )
    {
        m.Id(id).Version(1).StartsAt(ReminderState.Idle, () => new JsonObject());

        var open = m.In(ReminderState.Idle).Context().On(ReminderTrigger.Open);
        (gated ? open.Requires("Editor") : open).To(ReminderState.Review);

        // Once one event declares a rule, every event a client sends declares one.
        var approve = m.In(ReminderState.Review)
            .Context()
            .After(ReviewFor)
            .GoTo(ReminderState.Expired)
            .On(ReminderTrigger.Approve)
            .WithInput<ApproveInput>()
            .When(Input((ApproveInput i) => i.Verdict).IsOneOf("approved"))
            .Because("Only an approved verdict closes the review.");
        var extend = (gated ? approve.AnyAuthenticated() : approve)
            .To(ReminderState.Done)
            .On(ReminderTrigger.Extend);
        (gated ? extend.AnyAuthenticated() : extend).To(ReminderState.Review);

        m.In(ReminderState.Expired).Context().After(ExpiredFor).GoTo(ReminderState.Archived);
        m.In(ReminderState.Done).Context();
        m.In(ReminderState.Archived).Context();
    }

    /// <summary>A snapshot of the machine under <paramref name="machine"/> as a client sends it.</summary>
    public static string Json(string state, string machine = Id) =>
        new JsonObject
        {
            ["machine"] = machine,
            ["version"] = 1,
            ["state"] = state,
            ["context"] = new JsonObject(),
        }.ToJsonString();
}
