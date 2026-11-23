using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trax.Core.Decisions;
using Trax.Core.Exceptions;
using Trax.Core.Functional;
using Trax.Core.Monad;
using Trax.Effect.Attributes;
using Trax.Effect.Data.Decisions;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.JunctionEvents;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.JunctionRun;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.Decisions;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.FailureClassifier;
using Trax.Effect.Services.JunctionEvents;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Effect.Tests.Data.InMemory.Integration.IntegrationTests;

/// <summary>
/// What <see cref="OneAtATimeHandler"/> saw: whether any instance was used from two threads at
/// once, and the junctions whose events it handled.
/// </summary>
internal static class BranchMeeting
{
    private static TaskCompletionSource _a = New();
    private static TaskCompletionSource _b = New();

    public static bool ConcurrentUse;
    public static ConcurrentQueue<string> Handled = new();

    public static void Reset()
    {
        _a = New();
        _b = New();
        ConcurrentUse = false;
        Handled = new();
    }

    /// <summary>
    /// Holds a branch junction's start event until the other branch's has arrived too, so the
    /// two are handled at the same time.
    /// </summary>
    public static Task Meet(string junction)
    {
        var (mine, theirs) = string.Equals(junction, nameof(MeetA), StringComparison.Ordinal)
            ? (_a, _b)
            : (_b, _a);
        mine.TrySetResult();
        return theirs.Task.WaitAsync(TimeSpan.FromSeconds(30), TimeProvider.System);
    }

    private static TaskCompletionSource New() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
