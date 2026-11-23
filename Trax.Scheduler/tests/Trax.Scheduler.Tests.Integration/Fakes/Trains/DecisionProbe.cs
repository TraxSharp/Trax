using System.Collections.Concurrent;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Fakes.Trains;

/// <summary>What the probe train's runs did, shared because the scheduler builds the junctions.</summary>
public static class DecisionProbe
{
    /// <summary>Where the next run fails.</summary>
    public static ProbeFailure FailAt { get; set; }

    /// <summary>Each track taken, in order, as <c>(input value, track)</c>.</summary>
    public static ConcurrentQueue<(string Value, string Track)> Taken { get; } = new();

    public static void Reset()
    {
        FailAt = ProbeFailure.None;
        Taken.Clear();
    }

    public static IReadOnlyList<string> TracksOf(string value) =>
        Taken
            .Where(t => string.Equals(t.Value, value, StringComparison.Ordinal))
            .Select(t => t.Track)
            .ToList();
}
