// What the examples under sdk-reference/configuration/ take as given: the namespaces of the
// builder extensions they chain, and a train for the mediator to find. The host (services,
// connectionString, rabbitMqConnectionString, configuration) comes from _shared.cs.
global using Trax.Core.Functional;
global using Trax.Effect.Broadcaster.RabbitMQ.Extensions;
global using Trax.Effect.Broadcaster.SignalR.Extensions;
global using Trax.Effect.Configuration.BroadcasterBuilder;
global using Trax.Effect.Configuration.TraxBuilder;
global using Trax.Effect.Configuration.TraxEffectBuilder;
global using Trax.Effect.Data.Extensions;
global using Trax.Effect.Data.InMemory.Extensions;
global using Trax.Effect.Data.Postgres.Extensions;
global using Trax.Effect.Data.Sqlite.Extensions;
global using Trax.Effect.Decisions.SystemOne.Extensions;
global using Trax.Effect.Extensions;
global using Trax.Effect.JunctionProvider.Logging.Extensions;
global using Trax.Effect.JunctionProvider.Progress.Extensions;
global using Trax.Effect.Provider.Json.Extensions;
global using Trax.Effect.Provider.Parameter.Extensions;
global using Trax.Effect.Services.ServiceTrain;
global using Trax.Mediator.Extensions;
global using Trax.Scheduler.Extensions;

public record ShipOrderInput(string OrderId);

public interface IShipOrderTrain : IServiceTrain<ShipOrderInput, Unit>;

public record AuditInput(string Subject);

public interface IRunAuditTrain : IServiceTrain<AuditInput, Unit>;
