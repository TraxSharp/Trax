using System.Globalization;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.Tests.Fakes;

/// <summary>What the resumable trains ran, and where the next run fails.</summary>
public static class ResumeProbe
{
    /// <summary>Held only in the checkpoint's state: never in an input, an output or a message.</summary>
    public const string StateSecret = "CHECKPOINT-STATE-5e1d9a";

    private static readonly Lock Gate = new();
    private static readonly List<string> RanList = [];

    /// <summary>The junction that throws when it runs, or null.</summary>
    public static string? FailIn { get; set; }

    public static IReadOnlyList<string> Ran
    {
        get
        {
            lock (Gate)
                return [.. RanList];
        }
    }

    public static void Note(string junction)
    {
        lock (Gate)
            RanList.Add(junction);

        if (string.Equals(FailIn, junction, StringComparison.Ordinal))
            throw new TimeoutException($"{junction} timed out");
    }

    public static void Reset()
    {
        lock (Gate)
            RanList.Clear();
        FailIn = null;
    }
}
