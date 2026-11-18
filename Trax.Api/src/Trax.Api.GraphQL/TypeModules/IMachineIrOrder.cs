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
/// Orders the IR hashes of one machine by when each version was deployed, so a machine's push can tell
/// a row written by a newer version (which ends it) from one written by an older node mid-deploy (which does not).
/// Registered by the persisted operations, which number each version's documents as it registers them.
/// </summary>
internal interface IMachineIrOrder
{
    /// <summary>
    /// Whether <paramref name="irHash"/> is a newer version of <paramref name="machine"/> than this node runs, or null
    /// when this node cannot tell.
    /// </summary>
    bool? IsNewer(string machine, string irHash);
}
