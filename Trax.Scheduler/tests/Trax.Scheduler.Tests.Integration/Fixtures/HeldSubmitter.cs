using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Trax.Core.Functional;
using Trax.Effect.Configuration.TraxEffectBuilder;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Data.Sqlite.Extensions;
using Trax.Effect.Data.Testing;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.SnapshotDraft;
using Trax.Effect.Models.WorkQueue;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.ManifestManagerPollingService;
using Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;
using Trax.Scheduler.Trains.JobDispatcher;
using Trax.Scheduler.Trains.JobRunner;
using Trax.StateMachine.Persistence;

namespace Trax.Scheduler.Tests.Integration.Fixtures;

/// <summary>A submitter that holds each dispatched run for the test to run, on the host it chooses.</summary>
public sealed class HeldSubmitter(HeldJobs held) : IJobSubmitter
{
    public Task<string> EnqueueAsync(long metadataId)
    {
        held.Inputs[metadataId] = null;
        return Task.FromResult(string.Create(CultureInfo.InvariantCulture, $"held-{metadataId}"));
    }

    public Task<string> EnqueueAsync(long metadataId, object input)
    {
        held.Inputs[metadataId] = input;
        return Task.FromResult(string.Create(CultureInfo.InvariantCulture, $"held-{metadataId}"));
    }
}
