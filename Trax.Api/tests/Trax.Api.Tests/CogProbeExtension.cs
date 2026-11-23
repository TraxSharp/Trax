using System.Globalization;
using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.Services.HealthCheck;
using Trax.Effect.Attributes;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests;

[ExtendObjectType(typeof(Cog))]
public sealed class CogProbeExtension
{
    public int GetEchoId([Parent] Cog cog) => cog.Id;

    public string GetEchoName([Parent] Cog cog) =>
        string.IsNullOrEmpty(cog.Name) ? "<unset>" : cog.Name;
}
