using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Sqlite.Integration.Fakes.Trains;

public record FailingSchedulerTestInput : IManifestProperties
{
    public string FailureMessage { get; set; } = "Test failure";
}
