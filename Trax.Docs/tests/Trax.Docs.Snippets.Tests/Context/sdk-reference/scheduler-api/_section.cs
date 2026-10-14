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

public record ExtractInput : IManifestProperties
{
    public int Index { get; init; }
}

public record TransformInput : IManifestProperties
{
    public int Index { get; init; }
}

public record LoadInput : IManifestProperties
{
    public int Partition { get; init; }
}

public interface IExtractTrain : IServiceTrain<ExtractInput, Unit>;

public interface ITransformTrain : IServiceTrain<TransformInput, Unit>;

public interface IValidateTrain : IServiceTrain<TransformInput, Unit>;

public interface ILoadTrain : IServiceTrain<LoadInput, Unit>;

public record SyncTableInput : IManifestProperties
{
    public string TableName { get; init; } = "";
}

public interface ISyncTableTrain : IServiceTrain<SyncTableInput, Unit>;

public record Tenant(string Id, string Name);

public record SyncTenantInput : IManifestProperties
{
    public string TenantId { get; init; } = "";
    public string Name { get; init; } = "";
}

public interface ISyncTenantTrain : IServiceTrain<SyncTenantInput, Unit>;

public record SendReminderInput : IManifestProperties
{
    public string UserId { get; init; } = "";
}

public interface ISendReminderTrain : IServiceTrain<SendReminderInput, Unit>;

public record ScheduleOnceRequest(string ExternalId, string UserId, int DelayMinutes);
