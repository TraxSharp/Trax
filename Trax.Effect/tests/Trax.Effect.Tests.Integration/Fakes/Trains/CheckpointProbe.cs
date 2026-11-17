using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Tests.Integration.Fakes.Trains;

/// <summary>
/// Trains for the checkpoint and resume tests, shaped like the Recovery sample's research chain:
/// plan, route to a source, fetch, checkpoint, score, checkpoint, summarise. <see cref="Probe"/>
/// records which junctions ran and arms the failures.
/// </summary>
/// <remarks>
/// Its state is static, shared by every test in the process, so a fixture that uses it must be
/// <c>[NonParallelizable]</c>: two tests arming it at once would see each other's failures and
/// steps. <see cref="Reset"/> and every setter refuse a fixture that is not.
/// </remarks>
public static class CheckpointProbe
{
    private static readonly Lock Gate = new();

    private static string? _failIn;
    private static CountdownEvent? _bothBranches;
    private static Func<int>? _saveCount;
    private static DirtyMode _dirty;
    private static string _padding = "";

    public static List<string> Ran { get; } = [];

    /// <summary>The junction that throws when it runs, or null.</summary>
    public static string? FailIn
    {
        get => _failIn;
        set => _failIn = Guarded(value);
    }

    /// <summary>How <see cref="MakeDirty"/> leaves the step's data context.</summary>
    public static DirtyMode Dirty
    {
        get => _dirty;
        set => _dirty = Guarded(value);
    }

    /// <summary>The pages <see cref="FetchFullTexts"/> reports; a large value makes a large state.</summary>
    public static string Padding
    {
        get => _padding;
        set => _padding = Guarded(value);
    }

    /// <summary>Released once both branches of <see cref="TwoBranchTrain"/> reach their checkpoint.</summary>
    public static CountdownEvent? BothBranches
    {
        get => _bothBranches;
        set => _bothBranches = Guarded(value);
    }

    /// <summary>Reads how many saves the effect providers have made, when a test counts them.</summary>
    public static Func<int>? SaveCount
    {
        get => _saveCount;
        set => _saveCount = Guarded(value);
    }

    /// <summary>
    /// Fails fast when the running test's fixture could run beside another test, since the probe's
    /// state is shared by all of them.
    /// </summary>
    private static T Guarded<T>(T value)
    {
        var name = TestContext.CurrentContext.Test.ClassName;
        var fixture = name is null ? null : typeof(CheckpointProbe).Assembly.GetType(name);

        if (
            fixture is not null
            && !fixture.IsDefined(typeof(NonParallelizableAttribute), inherit: true)
        )
            throw new InvalidOperationException(
                $"{fixture.Name} uses {nameof(CheckpointProbe)}, whose state every test shares, "
                    + "so it must be [NonParallelizable]."
            );

        return value;
    }

    /// <summary>The save count each junction saw when it ran.</summary>
    public static Dictionary<string, int> SavesAt { get; } = [];

    public static void Note(string junction)
    {
        lock (Gate)
        {
            Ran.Add(junction);
            if (SaveCount is { } count)
                SavesAt[junction] = count();
        }

        if (string.Equals(FailIn, junction, StringComparison.Ordinal))
            throw new TimeoutException($"{junction} timed out");
    }

    public static void Reset()
    {
        lock (Gate)
        {
            Ran.Clear();
            SavesAt.Clear();
        }
        SaveCount = null;
        FailIn = null;
        Dirty = DirtyMode.Clean;
        Padding = "";
        BothBranches = null;
    }
}
