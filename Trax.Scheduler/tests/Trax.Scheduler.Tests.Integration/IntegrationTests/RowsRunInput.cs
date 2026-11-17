using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Tests.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>Input for <see cref="IRowsRunTrain"/>.</summary>
public record RowsRunInput : IManifestProperties
{
    public List<List<string>> Rows { get; set; } = [];
}
