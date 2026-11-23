using System.Collections.Concurrent;
using System.Globalization;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Fakes.Trains;

/// <summary>What the research train's runs did, shared because the scheduler builds the junctions.</summary>
public static class ResearchProbe
{
    /// <summary>The prefix of the manifest group the train writes before its checkpoint, one per topic.</summary>
    public const string NotePrefix = "research-note-";

    /// <summary>Holds the first junction to run, whichever it is, when given as <see cref="HoldIn"/>.</summary>
    public const string AnyJunction = "*";

    private static readonly TimeSpan HoldBound = TimeSpan.FromSeconds(30);

    private static TaskCompletionSource _released = NewGate();

    /// <summary>Each junction that ran, in order, as <c>(topic, junction)</c>.</summary>
    public static ConcurrentQueue<(string Topic, string Junction)> Ran { get; } = new();

    /// <summary>The junction that throws when it runs, or null.</summary>
    public static string? FailIn { get; set; }

    /// <summary>
    /// The junction that, once it starts, waits for <see cref="Release"/>; <see cref="AnyJunction"/>
    /// holds the first one to run. It holds once.
    /// </summary>
    public static string? HoldIn { get; set; }

    /// <summary>Released once when a junction is held.</summary>
    public static SemaphoreSlim Held { get; private set; } = new(0);

    public static IReadOnlyList<string> RanFor(string topic) =>
        Ran.Where(r => string.Equals(r.Topic, topic, StringComparison.Ordinal))
            .Select(r => r.Junction)
            .ToList();

    public static async Task Note(string topic, string junction)
    {
        Ran.Enqueue((topic, junction));

        if (
            HoldIn is { } hold
            && (
                string.Equals(hold, AnyJunction, StringComparison.Ordinal)
                || string.Equals(hold, junction, StringComparison.Ordinal)
            )
        )
        {
            HoldIn = null;
            Held.Release();
            await _released.Task.WaitAsync(HoldBound, TimeProvider.System).ConfigureAwait(false);
        }

        if (string.Equals(FailIn, junction, StringComparison.Ordinal))
            throw new TimeoutException($"{junction} timed out");
    }

    /// <summary>Lets the held junction go on.</summary>
    public static void Release() => _released.TrySetResult();

    public static void Reset()
    {
        Ran.Clear();
        FailIn = null;
        HoldIn = null;
        _released.TrySetResult();
        _released = NewGate();
        Held = new SemaphoreSlim(0);
    }

    private static TaskCompletionSource NewGate() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
