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

/// <summary>A scoped handler that, like one holding a database context, must not be used concurrently.</summary>
internal sealed class OneAtATimeHandler : IJunctionEventHandler
{
    private int _inUse;

    public async Task HandleAsync(TrainLifecycleEventMessage message, CancellationToken ct)
    {
        if (Interlocked.Increment(ref _inUse) > 1)
            BranchMeeting.ConcurrentUse = true;

        try
        {
            var name = message.Junction?.Name;
            if (
                string.Equals(
                    message.EventType,
                    TrainLifecycleEventMessage.JunctionStartedEventType,
                    StringComparison.Ordinal
                ) && name is nameof(MeetA) or nameof(MeetB)
            )
                await BranchMeeting.Meet(name).ConfigureAwait(false);

            if (name is not null)
                BranchMeeting.Handled.Enqueue(name);
        }
        finally
        {
            Interlocked.Decrement(ref _inUse);
        }
    }
}
