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

/// <summary>Key declared with <c>[Key]</c> on a property that is not named <c>Id</c>.</summary>
[TraxAllowAnonymous]
[TraxQueryModel(Name = "sprockets")]
public class Sprocket
{
    [System.ComponentModel.DataAnnotations.Key]
    public string Code { get; set; } = "";

    public string Label { get; set; } = "";
}
