using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Authorization;
using Trax.Api.GraphQL.Subscriptions;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Api.GraphQL.TypeModules;

/// <summary>
/// How many push streams a principal may hold open: <see cref="PerInstance"/> on one instance and
/// <see cref="PerPrincipal"/> in all, on this host. A stream holds its place until it ends.
/// </summary>
internal sealed class MachineWatchLimits
{
    /// <summary>The most streams one principal holds on one instance.</summary>
    public const int PerInstance = 10;

    /// <summary>The most streams one principal holds in all.</summary>
    public const int PerPrincipal = 2_000;

    private readonly Lock _gate = new();
    private readonly Dictionary<(string User, long RowId), int> _byInstance = [];
    private readonly Dictionary<string, int> _byPrincipal = new(StringComparer.Ordinal);

    /// <summary>The streams open now.</summary>
    public int Open
    {
        get
        {
            lock (_gate)
                return _byPrincipal.Values.Sum();
        }
    }

    /// <summary>A place for one more stream of <paramref name="user"/> on <paramref name="rowId"/>, or null when full.</summary>
    public IDisposable? TryAcquire(string user, long rowId)
    {
        lock (_gate)
        {
            var onInstance = _byInstance.GetValueOrDefault((user, rowId));
            var inAll = _byPrincipal.GetValueOrDefault(user);
            if (onInstance >= PerInstance || inAll >= PerPrincipal)
                return null;
            _byInstance[(user, rowId)] = onInstance + 1;
            _byPrincipal[user] = inAll + 1;
        }
        return new Lease(this, user, rowId);
    }

    private void Release(string user, long rowId)
    {
        lock (_gate)
        {
            if (_byInstance.TryGetValue((user, rowId), out var onInstance))
            {
                if (onInstance <= 1)
                    _byInstance.Remove((user, rowId));
                else
                    _byInstance[(user, rowId)] = onInstance - 1;
            }
            if (_byPrincipal.TryGetValue(user, out var inAll))
            {
                if (inAll <= 1)
                    _byPrincipal.Remove(user);
                else
                    _byPrincipal[user] = inAll - 1;
            }
        }
    }

    private sealed class Lease(MachineWatchLimits limits, string user, long rowId) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                limits.Release(user, rowId);
        }
    }
}
