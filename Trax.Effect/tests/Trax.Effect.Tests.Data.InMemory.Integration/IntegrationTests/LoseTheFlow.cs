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
/// Suppresses ExecutionContext flow and leaves it suppressed, as code that forgets to restore it
/// does. Every step of a chain built after it captures no flow, so each resumes without the run's.
/// </summary>
public static class LoseTheFlow
{
    /// <returns>A signal to set once the chain is built, which nothing runs on inline.</returns>
    public static TaskCompletionSource Now()
    {
        ExecutionContext.SuppressFlow();
        return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
