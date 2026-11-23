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
[TraxQueryModel(Name = "allScalarKinds")]
public class AllScalarKindsRow
{
    public int Id { get; set; }

    public short[] Shorts { get; set; } = [];
    public int[] Scores { get; set; } = [];
    public int?[] OptionalScores { get; set; } = [];
    public long[] Counts { get; set; } = [];
    public IList<byte> Bytes { get; set; } = [];
    public float[] Floats { get; set; } = [];
    public double[] Doubles { get; set; } = [];
    public decimal[] Amounts { get; set; } = [];
    public bool[] Flags { get; set; } = [];
    public string[] Tags { get; set; } = [];
    public Guid[] Keys { get; set; } = [];
    public DateTime[] Stamps { get; set; } = [];
    public DateTimeOffset[] Offsets { get; set; } = [];
    public Badge[] Badges { get; set; } = [];
}
