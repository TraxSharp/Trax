using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Execution.Configuration;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Trax.Api.Auth.ApiKey;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.Startup;
using Trax.Api.Services.HealthCheck;
using Trax.Effect.Attributes;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests;

/// <summary>
/// Targets RootMutation rather than RootQuery on purpose. A class-level attribute lands on the
/// type being extended, so on RootQuery it would gate every root field of every host in this
/// assembly that scans for type extensions. RootMutation exists only where a host asked for it.
/// </summary>
[ExtendObjectType("RootMutation")]
[TraxAuthorize]
public sealed class ClassGatedOnRootMutation
{
    public string ClassGatedRootField() => "gated";
}
