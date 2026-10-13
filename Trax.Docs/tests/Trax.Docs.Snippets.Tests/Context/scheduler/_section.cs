// What the examples on the scheduler pages take as given: the trains they schedule, their inputs,
// and an ITraxScheduler to call at runtime. Only the train interfaces exist, because scheduling a
// train needs its interface and input type, not its implementation. Each page names the Trax
// namespaces its own examples use in its page context file.
global using static SchedulerDocsHost;
using Trax.Core.Functional;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;
using Trax.Scheduler.Services.TraxScheduler;

public static class SchedulerDocsHost
{
    public static ITraxScheduler scheduler => serviceProvider.GetRequiredService<ITraxScheduler>();
}

public record SyncCustomersInput : IManifestProperties
{
    public string Region { get; init; } = "us-east";
    public int BatchSize { get; init; } = 1000;
}

public interface ISyncCustomersTrain : IServiceTrain<SyncCustomersInput, Unit>;

public record SyncTableInput : IManifestProperties
{
    public string TableName { get; init; } = "";
    public int SliceIndex { get; init; }
}

public interface ISyncTableTrain : IServiceTrain<SyncTableInput, Unit>;

public record ExtractInput : IManifestProperties
{
    public string Source { get; init; } = "";
    public string Table { get; init; } = "";
    public int Partition { get; init; }
}

public interface IExtractTrain : IServiceTrain<ExtractInput, Unit>;

public record TransformInput : IManifestProperties
{
    public string Format { get; init; } = "";
}

public interface ITransformTrain : IServiceTrain<TransformInput, Unit>;

public record LoadInput : IManifestProperties
{
    public string Table { get; init; } = "";
    public int Partition { get; init; }
}

public interface ILoadTrain : IServiceTrain<LoadInput, Unit>;

public record ValidateInput : IManifestProperties;

public interface IValidateTrain : IServiceTrain<ValidateInput, Unit>;

public record ReportInput : IManifestProperties
{
    public string Region { get; init; } = "";
}

public interface IReportTrain : IServiceTrain<ReportInput, Unit>;

public record SyncInput : IManifestProperties
{
    public string Source { get; init; } = "";
}

public interface ISyncTrain : IServiceTrain<SyncInput, Unit>;

public record SendReminderInput : IManifestProperties
{
    public string UserId { get; init; } = "";
    public string Message { get; init; } = "";
}

public interface ISendReminderTrain : IServiceTrain<SendReminderInput, Unit>;

public record ProcessRefundInput : IManifestProperties
{
    public int OrderId { get; init; }
}

public interface IProcessRefundTrain : IServiceTrain<ProcessRefundInput, Unit>;

public record PostDeployInput : IManifestProperties
{
    public string Version { get; init; } = "";
}

public interface IPostDeployTrain : IServiceTrain<PostDeployInput, Unit>;

public record CleanupInput : IManifestProperties
{
    public TimeSpan OlderThan { get; init; }
}

public interface ICleanupTrain : IServiceTrain<CleanupInput, Unit>;

public record ScraperInput : IManifestProperties;

public interface IScraperTrain : IServiceTrain<ScraperInput, Unit>;

public record ScoreLeadsInput : IManifestProperties;

public interface IScoreLeadsTrain : IServiceTrain<ScoreLeadsInput, Unit>;

public record HelloWorldInput : IManifestProperties
{
    public string Name { get; init; } = "";
}

public interface IHelloWorldTrain : IServiceTrain<HelloWorldInput, Unit>;

public record GoodbyeWorldInput : IManifestProperties
{
    public string Name { get; init; } = "";
}

public interface IGoodbyeWorldTrain : IServiceTrain<GoodbyeWorldInput, Unit>;
