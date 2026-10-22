// What the example on sdk-reference/statemachine-api/dotnet-client.md takes as given: the client's namespace, and a
// turnstile machine declared with the builder alone, as a frontend that shares the machine's code compiles it.
global using Trax.StateMachine.Client;

public enum TurnstileState
{
    Locked,
    Unlocked,
}

public enum TurnstileTrigger
{
    Coin,
    Push,
}

public sealed record CoinInput
{
    public string Coin { get; init; } = "";
}

public static class Turnstile
{
    public static BuiltMachine<TurnstileState, TurnstileTrigger> Build()
    {
        var m = new MachineBuilder<TurnstileState, TurnstileTrigger>();
        m.Id("turnstile").Version(1).StartsAt(TurnstileState.Locked, () => new JsonObject());
        m.In(TurnstileState.Locked)
            .Context()
            .On(TurnstileTrigger.Coin)
            .WithInput<CoinInput>()
            .To(TurnstileState.Unlocked);
        m.In(TurnstileState.Unlocked).Context().On(TurnstileTrigger.Push).To(TurnstileState.Locked);
        return m.Build();
    }
}
