using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

/// <summary>Where a <see cref="ObservedLauncher"/> fails the write it is part of.</summary>
public enum LaunchFault
{
    None,

    /// <summary>After the snapshot is written, before the run's entry is.</summary>
    BeforeEnqueue,

    /// <summary>After the run's entry is written, before the transaction commits.</summary>
    AfterEnqueue,

    /// <summary>Before the run's entry is written, as an authorization the caller fails.</summary>
    Forbidden,
}

/// <summary>
/// Wraps the mediator's launcher: runs an observation after the real enqueue (from another connection, while the
/// transaction is still open) and can fail the write on either side of it.
/// </summary>
internal sealed class ObservedLauncher(IInvokedTrainLauncher inner) : IInvokedTrainLauncher
{
    public static LaunchFault Fault { get; set; }

    public static Func<Task>? Observe { get; set; }

    /// <summary>Replaces the registered launcher with this wrapper around it.</summary>
    public static void Install(IServiceCollection services)
    {
        var registered = services.Last(d => d.ServiceType == typeof(IInvokedTrainLauncher));
        services.Remove(registered);
        var implementation = registered.ImplementationType!;
        services.AddScoped<IInvokedTrainLauncher>(sp => new ObservedLauncher(
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
        if (Fault == LaunchFault.Forbidden)
            throw new UnauthorizedAccessException("The caller may not run this train.");

        if (Fault == LaunchFault.BeforeEnqueue)
            throw new InvalidOperationException(
                "The host died between the advance and the enqueue."
            );

        await inner.Launch(launch, context, cancellationToken);

        if (Observe is { } observe)
            await observe();

        if (Fault == LaunchFault.AfterEnqueue)
            throw new InvalidOperationException("The host died before the commit.");
    }
}
