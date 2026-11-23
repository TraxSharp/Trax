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

/// <summary>What became of a run first found queued (<see cref="RunProgressAccess.QueuedFateAsync"/>).</summary>
internal enum QueuedFate
{
    /// <summary>Its entry is still queued.</summary>
    Waiting,

    /// <summary>It was dispatched, or ran: its lifecycle events say the rest.</summary>
    Dispatched,

    /// <summary>Its entry was cancelled before dispatch.</summary>
    Cancelled,

    /// <summary>Its entry is gone and no run was recorded under its id.</summary>
    Gone,
}
