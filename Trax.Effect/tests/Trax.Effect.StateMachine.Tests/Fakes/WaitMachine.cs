using System.Text.Json.Nodes;
using Ingest.Contracts;

namespace Trax.Effect.StateMachine.Tests.Fakes;

public enum WaitState
{
    Idle,
    Fetching,
    Fetched,
    FetchFailed,
    Cancelled,
    TimedOut,
}

public enum WaitTrigger
{
    Start,
    Retry,
}

/// <summary>
/// A timed invoking state: <c>Idle -(Start)-> Fetching</c>, where Fetching invokes <see cref="IFetchTrain"/> and
/// leaves for TimedOut once it has been held for the duration in the context's <c>patience</c> field, bounded by
/// one minute and one hour.
/// </summary>
public static class WaitMachine
{
    public sealed record WaitContext
    {
        public string Source { get; init; } = "";
        public TimeSpan? Patience { get; init; }
    }

    public static readonly TimeSpan Min = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan Max = TimeSpan.FromHours(1);

    public static readonly BuiltMachine<WaitState, WaitTrigger> Built = Build();
    public static SnapshotMachine<WaitState, WaitTrigger> Machine => Built.Engine;

    public static void Configure(IMachineBuilder<WaitState, WaitTrigger> m)
    {
        m.Id("wait")
            .Version(1)
            .StartsAt(
                WaitState.Idle,
                () => new JsonObject { ["source"] = "s3://bucket/a", ["patience"] = "PT5M" }
            );

        m.In(WaitState.Idle).Context<WaitContext>().On(WaitTrigger.Start).To(WaitState.Fetching);

        m.In(WaitState.Fetching)
            .Context<WaitContext>()
            .After<WaitContext>(c => c.Patience, Min, Max)
            .GoTo(WaitState.TimedOut)
            .Invokes<IFetchTrain, FetchInput, FetchOutput>(ctx => new FetchInput(
                ctx["source"]!.GetValue<string>()
            ))
            .OnDone(WaitState.Fetched)
            .OnFailed(WaitState.FetchFailed)
            .OnCancelled(WaitState.Cancelled);

        m.In(WaitState.Fetched).Context<WaitContext>();
        m.In(WaitState.FetchFailed)
            .Context<WaitContext>()
            .On(WaitTrigger.Retry)
            .To(WaitState.Fetching);
        m.In(WaitState.Cancelled).Context<WaitContext>();
        m.In(WaitState.TimedOut).Context<WaitContext>();
    }

    /// <summary>An Idle snapshot whose <c>patience</c> holds <paramref name="patience"/> as given.</summary>
    public static Snapshot IdleWith(JsonNode? patience) =>
        new()
        {
            Machine = "wait",
            Version = 1,
            State = nameof(WaitState.Idle),
            Context = new JsonObject { ["source"] = "s3://bucket/a", ["patience"] = patience },
        };

    private static BuiltMachine<WaitState, WaitTrigger> Build()
    {
        var m = new MachineBuilder<WaitState, WaitTrigger>();
        Configure(m);
        return m.Build();
    }
}
