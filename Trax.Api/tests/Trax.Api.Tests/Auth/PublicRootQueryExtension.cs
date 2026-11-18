using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using HotChocolate.Types;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Trax.Api.Auth.ApiKey;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.Services.HealthCheck;
using Trax.Effect.Attributes;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests.Auth;

/// <summary>
/// Two root-type extension fields, one of each posture. Both declare, because a root-type
/// extension field inherits nothing. <c>publicPing</c> stands in for the pre-login surfaces a
/// host cannot gate; <c>gatedPing</c> is what proves Trax's own attribute produces a working
/// gate and not just a schema annotation.
/// </summary>
[ExtendObjectType("RootQuery")]
public sealed class PublicRootQueryExtension
{
    [TraxAllowAnonymous]
    public string PublicPing() => "pong";

    [TraxAuthorize(Roles = "Admin")]
    public string GatedPing() => "pong";

    [TraxAuthorize]
    public string AuthenticatedPing() => "pong";
}
