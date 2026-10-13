// What the examples under sdk-reference/statemachine-api/ take as given: the state-machine usings, and a
// small checkout machine with its effect and the host's principal, as the State Machine sample declares them.
// The host (builder, services, connectionString) comes from _shared.cs.
global using System.Text.Json.Nodes;
global using Trax.Api.Auth;
global using Trax.Effect.Data.Postgres.Extensions;
global using Trax.Effect.Extensions;
global using Trax.Effect.Provider.Json.Extensions;
global using Trax.Effect.StateMachine;
global using Trax.Effect.StateMachine.Persistence;
global using static Trax.Effect.StateMachine.Rules;
global using Trax.Mediator.Extensions;

public enum CheckoutState
{
    Cart,
    Review,
    Paid,
}

public enum CheckoutTrigger
{
    Next,
    Back,
    Pay,
    Reset,
}

public sealed record CheckoutContext
{
    public List<string> Items { get; init; } = [];

    public string? Receipt { get; init; }
}

public sealed record PaidContext
{
    public List<string> Items { get; init; } = [];

    public string Receipt { get; init; } = "";
}

public sealed record PayInput
{
    public string Receipt { get; init; } = "";
}

public interface ICharge : ISnapshotEffect;

public interface IPaymentGateway
{
    Task<string> Charge(JsonObject order, string idempotencyKey, CancellationToken ct);
}

public sealed class CheckoutMachine : Machine<CheckoutState, CheckoutTrigger>
{
    protected override void Configure(IMachineBuilder<CheckoutState, CheckoutTrigger> m)
    {
        m.Id("checkout")
            .Version(1)
            .StartsAt(CheckoutState.Cart, () => new JsonObject { ["items"] = new JsonArray() });

        m.In(CheckoutState.Cart)
            .Context<CheckoutContext>()
            .On(CheckoutTrigger.Next)
            .When(Field((CheckoutContext c) => c.Items).CountAtLeast(1))
            .To(CheckoutState.Review);

        m.In(CheckoutState.Review)
            .Context<CheckoutContext>()
            .On(CheckoutTrigger.Pay)
            .WithInput<PayInput>()
            .RunsOnce<ICharge>()
            .Reduce(Set((PaidContext p) => p.Receipt).FromInput((PayInput i) => i.Receipt))
            .To(CheckoutState.Paid);

        m.In(CheckoutState.Paid).Context<PaidContext>().Committed();
    }
}

/// <summary>Maps Trax's authenticated caller to the user key that scopes drafts.</summary>
public sealed class TraxCallerSnapshotPrincipal(TraxCaller caller) : ISnapshotPrincipal
{
    public string? CurrentUserKey => caller.IsAuthenticated ? caller.Principal!.Id : null;
}
