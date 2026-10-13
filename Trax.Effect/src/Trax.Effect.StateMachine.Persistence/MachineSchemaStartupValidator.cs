using Microsoft.Extensions.Hosting;

namespace Trax.Effect.StateMachine.Persistence;

/// <summary>
/// Refuses to start a host whose machine cannot be built or fails unexpectedly computing its IR hash. Every write
/// to a machine's instance records the IR hash of the definition that made it (<see cref="IMachine.SchemaHash"/>),
/// so a machine whose hash throws would fail every write it makes; this computes each hash once, before any hosted
/// service starts, and names every machine that fails. A machine whose IR export is refused by design (raw
/// delegates, or rules mixed with a delegate guard, reducer or validator) has no hash, which is not a failure:
/// only serving it through typed operations is refused. A host that registers no machine at all, by the
/// <c>AddStateMachines</c> scan or by its own <see cref="IMachine"/> registration, is refused too.
/// </summary>
/// <remarks>
/// It runs in <see cref="StartingAsync"/>, which the host finishes for every hosted service before it calls any
/// <c>StartAsync</c>; something that calls only <c>StartAsync</c> gets the check from there.
/// </remarks>
internal sealed class MachineSchemaStartupValidator(IEnumerable<IMachine> machines)
    : IHostedLifecycleService
{
    private bool _checked;

    public Task StartingAsync(CancellationToken cancellationToken)
    {
        _checked = true;
        // Machines come from the AddStateMachines scan or from the host's own IMachine registrations, which may
        // follow the call, so whether there are any is known only once the container is built.
        if (!machines.Any())
            throw new InvalidOperationException(
                "No state machines are registered. Pass the assemblies that contain your "
                    + "Machine<TState, TTrigger> subclasses, e.g. trax.AddStateMachines(typeof(Program).Assembly), "
                    + "or register each machine as IMachine."
            );
        var problems = new List<(string Problem, Exception Cause)>();
        foreach (var machine in machines)
        {
            try
            {
                _ = machine.SchemaHash;
            }
            catch (Exception ex)
            {
                problems.Add(
                    (
                        $"{machine.GetType().FullName}: its IR hash could not be computed, so no write to it "
                            + $"could record the definition that made it. {ex.Message}",
                        ex
                    )
                );
            }
        }

        if (problems.Count > 0)
            throw new InvalidOperationException(
                $"{problems.Count} state machine(s) cannot be served:"
                    + Environment.NewLine
                    + string.Join(Environment.NewLine, problems.Select(p => "  - " + p.Problem)),
                problems.Count == 1
                    ? problems[0].Cause
                    : new AggregateException(problems.Select(p => p.Cause))
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
