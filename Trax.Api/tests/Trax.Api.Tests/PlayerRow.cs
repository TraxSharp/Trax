using System.Text.RegularExpressions;
using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Data;
using HotChocolate.Data.Filters;
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Api.GraphQL.Configuration;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.Filtering.ListElements;
using Trax.Api.GraphQL.Queries;
using Trax.Api.GraphQL.TypeModules;
using Trax.Api.Services.HealthCheck;
using Trax.Effect.Attributes;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests;

[TraxAllowAnonymous]
[TraxQueryModel(Name = "players")]
public class PlayerRow
{
    public int Id { get; set; }

    /// <summary>Scalar enum: keeps the full operation set, `neq` included.</summary>
    public Badge Tier { get; set; }

    /// <summary>Scalar string: same, and gives the schema a StringOperationFilterInput.</summary>
    public string Name { get; set; } = "";

    public Badge[] Badges { get; set; } = [];

    public string[] Tags { get; set; } = [];

    public int[] Scores { get; set; } = [];

    /// <summary>The IList&lt;T&gt; shape carries the same trap and is restricted too.</summary>
    public IList<Badge> LegacyBadges { get; set; } = [];
}
