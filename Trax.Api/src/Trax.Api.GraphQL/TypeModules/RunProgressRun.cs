using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Threading.Channels;
using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Execution;
using HotChocolate.Execution.Configuration;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using HotChocolate.Subscriptions;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Authorization;
using Trax.Api.GraphQL.Subscriptions;
using Trax.Api.GraphQL.Types;
using Trax.Effect.Data.JunctionEvents;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models;
using Trax.Mediator.Services.Principal;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Api.GraphQL.TypeModules;

/// <summary>One run a caller may follow, as <c>RunProgressAccess.FindAsync</c> found it.</summary>
internal sealed record RunProgressRun(
    string RunId,
    string TrainName,
    string? Field,
    long? MetadataId,
    RunProgressState State,
    string? Failure,
    string Starter,
    LifecycleVisibility Visibility
)
{
    /// <summary>The manifest the run is one occurrence of, or null.</summary>
    public long? ManifestId { get; init; }

    /// <summary>When the run ended, or null.</summary>
    public DateTime? EndedAt { get; init; }

    /// <summary>The run that retries this failed one, once it is queued.</summary>
    public string? RetriedAs { get; init; }
}
