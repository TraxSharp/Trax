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
/// The extension that puts one machine's push field on the subscription root, carrying the machine's id so
/// <see cref="MachinePushFieldCollisionInterceptor"/> can name it.
/// </summary>
internal sealed class MachinePushExtension(
    string machineId,
    Action<IObjectTypeDescriptor> configure
) : ObjectTypeExtension(configure)
{
    /// <summary>The id of the machine whose push this is.</summary>
    public string MachineId { get; } = machineId;
}
