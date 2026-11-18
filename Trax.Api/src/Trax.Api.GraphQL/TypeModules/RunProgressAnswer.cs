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

/// <summary>A run as its starter reads it: where it stands and the steps it took, or why it cannot be read.</summary>
/// <param name="RunId">The run's external id.</param>
/// <param name="Train">The train's field, as the schema serves it (<c>startRun</c>).</param>
/// <param name="State">Where the run stands, or null on a refusal.</param>
/// <param name="Steps">The steps the run recorded so far, in order, at most 500.</param>
/// <param name="Failure">Why the run failed, when a junction threw a <c>TrainException</c>; null otherwise.</param>
/// <param name="Problem">Why the run cannot be read, or null.</param>
/// <param name="RetriedAs">The id of the run that retries this failed one, once it is queued; null otherwise.</param>
internal sealed record RunProgressAnswer(
    string RunId,
    string? Train,
    RunProgressState? State,
    IReadOnlyList<JunctionStep> Steps,
    string? Failure,
    SnapshotProblem? Problem,
    string? RetriedAs = null
);
