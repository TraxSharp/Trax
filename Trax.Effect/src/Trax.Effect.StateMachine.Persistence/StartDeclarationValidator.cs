using Microsoft.Extensions.Hosting;

namespace Trax.Effect.StateMachine.Persistence;

/// <summary>
/// Refuses to start a host with a machine a client can reach (marked [TraxMachine], or user-owned while the generic mutations are
/// opted in) whose declared <c>start</c> copies its input into a field only the server writes: one an invoked train's outcome, the machine's effect or an event gated by <c>Requires</c> writes. A client
/// could otherwise start an instance already holding what it may not write. It runs in <see cref="StartingAsync"/>,
/// before any hosted service starts.
/// </summary>
internal sealed class StartDeclarationValidator(
    IEnumerable<IMachine> machines,
    StateMachineOptions options
) : IHostedLifecycleService
{
    private bool _checked;

    public Task StartingAsync(CancellationToken cancellationToken)
    {
        _checked = true;
        // Only a machine a client can reach is started by a client: one marked [TraxMachine], or every user-owned one
        // while the generic mutations are opted in.
        var problems = machines
            .Where(m =>
                StateMachineOptions.IsServed(m)
                || (
                    options.GenericMutationsExposed && m is IMachineInternals { SystemOwned: false }
                )
            )
            .OfType<IMachineInternals>()
            .SelectMany(m => m.StartProblems)
            .ToList();
        if (problems.Count > 0)
            throw new InvalidOperationException(
                $"{problems.Count} machine start(s) reach a field only the server writes:"
                    + Environment.NewLine
                    + string.Join(Environment.NewLine, problems.Select(p => "  - " + p))
            );
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken) =>
        _checked ? Task.CompletedTask : StartingAsync(cancellationToken);

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
