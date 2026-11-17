using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Core.Decisions;
using Trax.Core.Exceptions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Data.Decisions;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.Decisions;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.EffectProvider;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Tests.Data.InMemory.Integration.IntegrationTests;

/// <summary>
/// Holds each run until the expected number have arrived. Each subclass is a barrier of its own,
/// since a generic type's statics are per closed type.
/// </summary>
public abstract class Meeting<TSelf> : Junction<Order, Order>
    where TSelf : Meeting<TSelf>
{
    private static int _expected;
    private static int _arrived;
    private static TaskCompletionSource _all = new();

    public static void Expect(int runs)
    {
        _expected = runs;
        _arrived = 0;
        _all = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public override async Task<Order> Run(Order input)
    {
        var all = _all;

        if (Interlocked.Increment(ref _arrived) == _expected)
            all.TrySetResult();

        await all.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        return input;
    }
}
