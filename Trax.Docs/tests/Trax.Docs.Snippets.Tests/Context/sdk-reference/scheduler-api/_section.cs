// What the examples under sdk-reference/scheduler-api take as given: the usings and the trains
// they schedule. The host (services, connectionString, assembly) comes from _shared.cs.
global using Trax.Core.Functional;
global using Trax.Effect.Data.Postgres.Extensions;
global using Trax.Effect.Enums;
global using Trax.Effect.Extensions;
global using Trax.Effect.Models.Manifest;
global using Trax.Effect.Services.ServiceTrain;
global using Trax.Mediator.Extensions;
global using Trax.Scheduler.Configuration;
global using Trax.Scheduler.Extensions;
global using Trax.Scheduler.Services.Scheduling;
global using Trax.Scheduler.Services.TraxScheduler;

public record SyncInput : IManifestProperties
{
    public string Source { get; init; } = "";
}

public interface ISyncTrain : IServiceTrain<SyncInput, Unit>;

public record ReportInput : IManifestProperties
{
    public string Region { get; init; } = "";
}

public interface IBuildReportTrain : IServiceTrain<ReportInput, Unit>;

public interface ISendReportTrain : IServiceTrain<ReportInput, Unit>;

public record ContentInput : IManifestProperties
{
    public string ContentId { get; init; } = "";
}

public interface IReviewContentTrain : IServiceTrain<ContentInput, Unit>;

public interface ISendViolationNoticeTrain : IServiceTrain<ContentInput, Unit>;

public interface IBatchProcessTrain : IServiceTrain<ContentInput, Unit>;
