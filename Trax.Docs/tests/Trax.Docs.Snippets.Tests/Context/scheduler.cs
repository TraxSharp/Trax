// What the examples on scheduler.md take as given: the usings of a scheduler host, the trains
// they schedule and an ITraxScheduler to call.
global using static SchedulerOverviewHost;
global using Trax.Core.Functional;
global using Trax.Effect.Data.Postgres.Extensions;
global using Trax.Effect.Extensions;
global using Trax.Mediator.Extensions;
global using Trax.Scheduler.Extensions;
global using Trax.Scheduler.Services.Scheduling;
global using Trax.Scheduler.Services.TraxScheduler;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

public static class SchedulerOverviewHost
{
    public static ITraxScheduler scheduler => serviceProvider.GetRequiredService<ITraxScheduler>();
}

public record SyncCustomersInput : IManifestProperties
{
    public string Region { get; init; } = "";
    public int BatchSize { get; init; }
}

public interface ISyncCustomersTrain : IServiceTrain<SyncCustomersInput, Unit>;

public record SyncTableInput : IManifestProperties
{
    public string TableName { get; init; } = "";
}

public interface ISyncTableTrain : IServiceTrain<SyncTableInput, Unit>;

public record SendReminderInput : IManifestProperties
{
    public string UserId { get; init; } = "";
}

public interface ISendReminderTrain : IServiceTrain<SendReminderInput, Unit>;

public record ExtractInput : IManifestProperties;

public interface IExtractTrain : IServiceTrain<ExtractInput, Unit>;

public record LoadInput : IManifestProperties;

public interface ILoadTrain : IServiceTrain<LoadInput, Unit>;
