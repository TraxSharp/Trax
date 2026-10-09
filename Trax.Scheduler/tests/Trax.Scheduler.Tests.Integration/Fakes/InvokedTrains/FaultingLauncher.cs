using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;

/// <summary>
/// Wraps the mediator's launcher and, while <see cref="Armed"/>, fails a launch an outcome makes after the run's
/// entry is written and before the transaction commits: a host dying between the two halves of an outcome.
/// </summary>
internal sealed class FaultingLauncher(IInvokedTrainLauncher inner) : IInvokedTrainLauncher
{
    public static bool Armed { get; set; }

    /// <summary>Replaces the registered launcher with this wrapper around it.</summary>
    public static void Install(IServiceCollection services)
    {
        var registered = services.Last(d => d.ServiceType == typeof(IInvokedTrainLauncher));
        services.Remove(registered);
        var implementation = registered.ImplementationType!;
        services.AddScoped<IInvokedTrainLauncher>(sp => new FaultingLauncher(
            (IInvokedTrainLauncher)ActivatorUtilities.CreateInstance(sp, implementation)
        ));
    }

    public IReadOnlyList<string> Refusals(
        InvokedTrainDeclaration declaration,
        IServiceProvider services
    ) => inner.Refusals(declaration, services);

    public async Task Launch(
        InvokedTrainLaunch launch,
        IDataContext context,
        CancellationToken cancellationToken = default
    )
    {
        await inner.Launch(launch, context, cancellationToken);

        if (Armed && launch.FromOutcome)
            throw new InvalidOperationException("The host died before the outcome committed.");
    }
}
