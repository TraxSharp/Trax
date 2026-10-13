---
title: Add your own trains to a template
description: "Add a query train served over GraphQL and a scheduled train with its manifest to a project made from the trax-hub, trax-api or trax-scheduler template."
type: how-to
area: tooling
nav_order: 3
source:
  - Trax.Samples/templates/content
---

# Add your own trains to a template

The steps below assume a project made from one of the
[project templates](/docs/reference/templates). A query train needs `trax-hub` or `trax-api`
(they serve GraphQL); a scheduled train needs `trax-hub` or `trax-scheduler` (they run the
scheduler).

Put each train in its own folder under `Trains/`, with its interface, input, output and a
`Junctions/` folder, laid out as `HelloWorld/` is. `AddMediator(typeof(Program).Assembly)` in
`Program.cs` finds it; nothing else is registered.

## Add a query train

1. Add the train and its interface. `[TraxQuery]` serves it under `discover`, and every train the
   API exposes carries `[TraxAuthorize]` or `[TraxAllowAnonymous]`; the template's demo key holds
   the `User` role.

   ```csharp compile=app
   using Trax.Core.Functional;
   using Trax.Effect.Attributes;
   using Trax.Effect.Services.ServiceTrain;

   namespace MyCompany.Hub.Trains.GetCustomer;

   public interface IGetCustomerTrain : IServiceTrain<GetCustomerInput, CustomerOutput>;

   [TraxAuthorize(Roles = "User")]
   [TraxQuery(Description = "Fetches a customer by ID")]
   public class GetCustomerTrain
       : ServiceTrain<GetCustomerInput, CustomerOutput>, IGetCustomerTrain
   {
       protected override Task<Either<Exception, CustomerOutput>> Junctions() =>
           Chain<FetchCustomerJunction>().Resolve();
   }
   ```

2. Add its input, output and junction. Derive `EffectJunction<TIn, TOut>`, as the template's
   junctions do, so `AddJunctionProgress()` reports it.

   ```csharp compile=app
   using Trax.Effect.Services.EffectJunction;

   namespace MyCompany.Hub.Trains.GetCustomer;

   public record GetCustomerInput
   {
       public required string Id { get; init; }
   }

   public record CustomerOutput
   {
       public required string Id { get; init; }
       public required string Name { get; init; }
   }

   public class FetchCustomerJunction : EffectJunction<GetCustomerInput, CustomerOutput>
   {
       public override Task<CustomerOutput> Run(GetCustomerInput input) =>
           Task.FromResult(new CustomerOutput { Id = input.Id, Name = $"Customer {input.Id}" });
   }
   ```

3. Run the host and call the field. It is `getCustomer` under `query { discover { ... } }`: the
   interface name without the `I` and the `Train`, in camelCase.

   ```bash
   curl -X POST http://localhost:5400/trax/graphql \
     -H 'Content-Type: application/json' \
     -H 'X-Api-Key: demo-key-do-not-use-in-production' \
     -d '{"query":"{ discover { getCustomer(input: { id: \"42\" }) { id name } } }"}'
   ```

The host refuses to start if an exposed train has neither `[TraxAuthorize]` nor
`[TraxAllowAnonymous]`, and if a junction needs an input that nothing before it produces.

## Add a scheduled train

1. Add the train. A scheduled train's input implements `IManifestProperties`, so the scheduler can
   store it on the manifest.

   ```csharp compile=app
   using Trax.Core.Functional;
   using Trax.Effect.Models.Manifest;
   using Trax.Effect.Services.ServiceTrain;

   namespace MyCompany.Scheduler.Trains.SyncCustomers;

   public record SyncCustomersInput : IManifestProperties
   {
       public string Region { get; init; } = "us-east";
   }

   public interface ISyncCustomersTrain : IServiceTrain<SyncCustomersInput, Unit>;

   public class SyncCustomersTrain : ServiceTrain<SyncCustomersInput, Unit>, ISyncCustomersTrain
   {
       protected override Task<Either<Exception, Unit>> Junctions() =>
           Chain<FetchCustomersJunction>()
               .Chain<UpsertCustomersJunction>()
               .Resolve();
   }
   ```

2. Add `FetchCustomersJunction` and `UpsertCustomersJunction` under the train's `Junctions/`
   folder, as in step 2 above: the first takes the `SyncCustomersInput`, the last returns `Unit`.

3. In `Program.cs`, add the schedule after the `hello-world` one, inside `AddScheduler`:

   ```csharp compile=app
   scheduler
       .Schedule<ISyncCustomersTrain>(
           "sync-customers",
           new SyncCustomersInput { Region = "us-east" },
           Every.Hours(1)
       );
   ```

The first argument is the manifest's id. `Schedule` updates the manifest with that id at every
startup, so renaming it creates a new manifest and leaves the old one in place. The dashboard
shows the new manifest and each run.

See [Scheduling](/docs/scheduler) for cron schedules, dependent trains, bulk scheduling and dead
letters, and [Trains & Junctions](/docs/core/trains-and-junctions) for what a junction can do.

## SDK Reference

> [AddMediator](/docs/sdk-reference/configuration/add-mediator) | [TraxQuery / TraxMutation](/docs/sdk-reference/graphql-api/trax-graphql-attribute) | [TraxAuthorize](/docs/sdk-reference/attributes/trax-authorize) | [Schedule](/docs/sdk-reference/scheduler-api/schedule) | [EffectJunction](/docs/sdk-reference/trains-and-junctions/effect-junction) | [ServiceTrain](/docs/sdk-reference/trains-and-junctions/service-train)
