// What the examples on scheduler/cancellation-and-timeouts.md take as given: the usings and the
// train they schedule. The service collection, assembly and connection string come from _shared.cs.
global using Trax.Core.Functional;
global using Trax.Effect.Data.Postgres.Extensions;
global using Trax.Effect.Extensions;
global using Trax.Effect.Models.Manifest;
global using Trax.Effect.Services.ServiceTrain;
global using Trax.Mediator.Extensions;
global using Trax.Scheduler.Extensions;
global using Trax.Scheduler.Services.Scheduling;

public record SyncInput : IManifestProperties;

public interface ISyncTrain : IServiceTrain<SyncInput, Unit>;
