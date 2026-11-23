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

/// <summary>
/// Input for <see cref="IPaddedRunTrain"/>. A caller may send <c>{}</c>, but the stored form
/// writes every member, and the default padding is larger than the stored-input cap.
/// </summary>
public record PaddedRunInput : IManifestProperties
{
    public string Padding { get; set; } = new('x', 1_100_000);
}
