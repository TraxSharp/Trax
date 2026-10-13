// What the compiled examples on reference/add-trains-to-a-template.md take as given: the scheduled
// train's two junctions, and the scheduler builder the Program.cs line is written inside.
global using MyCompany.Scheduler.Trains.SyncCustomers;
global using static TemplateScheduler;
global using Trax.Scheduler.Services.Scheduling;
using Trax.Core.Functional;
using Trax.Effect.Services.EffectJunction;
using Trax.Scheduler.Configuration;

public static class TemplateScheduler
{
    public static SchedulerConfigurationBuilder scheduler => null!;
}

namespace MyCompany.Scheduler.Trains.SyncCustomers
{
    public record CustomerBatch(string Region);

    public class FetchCustomersJunction : EffectJunction<SyncCustomersInput, CustomerBatch>
    {
        public override Task<CustomerBatch> Run(SyncCustomersInput input) =>
            Task.FromResult(new CustomerBatch(input.Region));
    }

    public class UpsertCustomersJunction : EffectJunction<CustomerBatch, Unit>
    {
        public override Task<Unit> Run(CustomerBatch input) => Task.FromResult(Unit.Default);
    }
}
