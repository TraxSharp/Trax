// What the examples on sdk-reference/scheduler-api/schedule.md take as given: the usings, a
// service collection, a connection string, and the train they schedule.
global using static SnippetContext;
global using Trax.Core.Functional;
global using Trax.Effect.Data.Postgres.Extensions;
global using Trax.Effect.Extensions;
global using Trax.Effect.Models.Manifest;
global using Trax.Effect.Services.ServiceTrain;
global using Trax.Mediator.Extensions;
global using Trax.Scheduler.Extensions;
global using Trax.Scheduler.Services.Scheduling;
global using Trax.Scheduler.Services.TraxScheduler;

public record SyncInput : IManifestProperties
{
    public string Source { get; init; } = "";
}

public interface ISyncTrain : IServiceTrain<SyncInput, Unit>;

public static class SnippetContext
{
    public static IServiceCollection services { get; } = new ServiceCollection();

    public static string connectionString => "Host=localhost;Database=trax";
}
