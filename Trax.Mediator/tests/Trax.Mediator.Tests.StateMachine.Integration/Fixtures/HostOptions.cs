using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Sqlite.Extensions;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainAuthorization;
using Trax.Mediator.Tests.StateMachine.Integration.Fakes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fixtures;

/// <summary>What a test host registers beyond the provider.</summary>
public sealed record HostOptions
{
    public bool Mediator { get; init; } = true;

    public bool JunctionProgress { get; init; } = true;

    /// <summary>Registrations made after Trax's, so the last one wins.</summary>
    public Action<IServiceCollection>? Configure { get; init; }
}
