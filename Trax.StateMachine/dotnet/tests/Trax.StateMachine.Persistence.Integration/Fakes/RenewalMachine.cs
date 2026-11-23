using System.Text.Json.Nodes;
using Trax.Effect.Services.ServiceTrain;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>
/// A user's subscription whose irreversible charge lands in the timed <c>Active</c>, which lapses after
/// <see cref="ActiveFor"/>: the effect runner's write enters a timed state.
/// </summary>
public sealed class RenewalMachine : Machine<RenewalState, RenewalTrigger>
{
    public const string Id = "renewal";

    public static readonly TimeSpan ActiveFor = TimeSpan.FromDays(30);

    protected override void Configure(IMachineBuilder<RenewalState, RenewalTrigger> m)
    {
        m.Id(Id).Version(1).StartsAt(RenewalState.Draft, () => new JsonObject());

        m.In(RenewalState.Draft)
            .On(RenewalTrigger.Charge)
            .RunsOnce<IOrderCharge>()
            .Reduce((_, input) => new JsonObject { ["receipt"] = input?["receipt"]?.DeepClone() })
            .To(RenewalState.Active);

        m.In(RenewalState.Active).After(ActiveFor).GoTo(RenewalState.Lapsed);
    }

    public static string DraftJson =>
        new JsonObject
        {
            ["machine"] = Id,
            ["version"] = 1,
            ["state"] = "Draft",
            ["context"] = new JsonObject(),
        }.ToJsonString();
}
