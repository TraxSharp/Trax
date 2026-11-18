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

/// <summary>
/// How many runProgress streams a caller may hold open on this host: <see cref="PerRun"/> on one run and
/// <see cref="PerPrincipal"/> in all. A stream holds its place until it ends.
/// </summary>
internal sealed class RunProgressLimits
{
    /// <summary>The most streams one caller holds on one run.</summary>
    public const int PerRun = 10;

    /// <summary>The most streams one caller holds in all.</summary>
    public const int PerPrincipal = 500;

    private readonly Lock _gate = new();
    private readonly Dictionary<(string Starter, string RunId), int> _byRun = [];
    private readonly Dictionary<string, int> _byStarter = new(StringComparer.Ordinal);

    /// <summary>A place for one more stream of <paramref name="starter"/> on <paramref name="runId"/>, or null when full.</summary>
    public IDisposable? TryAcquire(string starter, string runId)
    {
        lock (_gate)
        {
            var onRun = _byRun.GetValueOrDefault((starter, runId));
            var inAll = _byStarter.GetValueOrDefault(starter);
            if (onRun >= PerRun || inAll >= PerPrincipal)
                return null;
            _byRun[(starter, runId)] = onRun + 1;
            _byStarter[starter] = inAll + 1;
        }
        return new Lease(this, starter, runId);
    }

    private void Release(string starter, string runId)
    {
        lock (_gate)
        {
            if (_byRun.TryGetValue((starter, runId), out var onRun))
            {
                if (onRun <= 1)
                    _byRun.Remove((starter, runId));
                else
                    _byRun[(starter, runId)] = onRun - 1;
            }
            if (_byStarter.TryGetValue(starter, out var inAll))
            {
                if (inAll <= 1)
                    _byStarter.Remove(starter);
                else
                    _byStarter[starter] = inAll - 1;
            }
        }
    }

    private sealed class Lease(RunProgressLimits limits, string starter, string runId) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                limits.Release(starter, runId);
        }
    }
}
