using Microsoft.Extensions.Hosting;
using Trax.Effect.Configuration.ClientExport;

namespace Trax.Effect.StateMachine.Persistence;

/// <summary>
/// Refuses to start a host that registers two machine types under one id (<see cref="IMachine.Name"/>). Every machine
/// is looked up by its id, by the registry each request builds, by the typed surface and by the background services, so
/// two types under one id would answer every machine request, unrelated machines included, with an internal error. A
/// subclass has its base's id, so the usual cause is serving a machine by deriving a marked class from it while the
/// base is registered too (a scan of the library's assembly, or the library's own registration): the refusal then names
/// both and says to register only the subclass. The same type registered twice is one machine, not a clash.
/// </summary>
/// <remarks>
/// It runs in <see cref="StartingAsync"/>, which the host finishes for every hosted service before it calls any
/// <c>StartAsync</c>; something that calls only <c>StartAsync</c> gets the check from there. A machine that cannot be
/// built is left to <see cref="MachineSchemaStartupValidator"/>, which names it.
/// See <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.
/// </remarks>
internal sealed class MachineIdStartupValidator(IEnumerable<IMachine> machines)
    : IHostedLifecycleService,
        ITraxStartupCheck
{
    private bool _checked;

    public Task StartingAsync(CancellationToken cancellationToken)
    {
        _checked = true;
        var problems = Problems(machines);
        if (problems.Count > 0)
            throw new InvalidOperationException(
                $"{problems.Count} machine id(s) are each declared by more than one registered machine type. Every "
                    + "machine is looked up by its id, so this host would answer every machine request with an "
                    + "internal error:"
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

    /// <summary>One sentence for each id more than one registered machine type declares; empty when there is none.</summary>
    internal static IReadOnlyList<string> Problems(IEnumerable<IMachine> machines)
    {
        var named = new List<(string Name, Type Type)>();
        foreach (var machine in RegisteredMachines.Distinct(machines))
        {
            string name;
            try
            {
                name = machine.Name;
            }
            catch (Exception)
            {
                // A machine that cannot be built has no id to clash on; the schema check names it.
                continue;
            }
            named.Add((name, machine.GetType()));
        }

        var problems = new List<string>();
        foreach (
            var clash in named
                .GroupBy(m => m.Name, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
        )
        {
            var types = clash
                .Select(m => m.Type)
                .OrderBy(t => t.FullName, StringComparer.Ordinal)
                .ToList();
            var names = string.Join(" and ", types.Select(t => t.FullName));
            // A subclass has its base's id: the one type every other derives from is the one to keep.
            var leaf = types.SingleOrDefault(t =>
                types.All(other => other == t || other.IsAssignableFrom(t))
            );
            var others = types.Count == 2 ? "the other" : "the others";
            problems.Add(
                leaf is not null
                    ? $"The id '{clash.Key}' is declared by {names}. {leaf.FullName} derives from {others}, so it "
                        + $"has its id. Register only {leaf.FullName}, not what it derives from: to serve a "
                        + "machine you do not own, derive a class from it, mark that [TraxMachine], and do not "
                        + "register the base (do not scan the assembly that holds it, and do not call a registration "
                        + "of the library's that adds it)."
                    : $"The id '{clash.Key}' is declared by {names}. Give one of them another id, or register only "
                        + "one of them."
            );
        }
        return problems;
    }
}

/// <summary>The registered machines, each type once: the same type registered twice is one machine.</summary>
internal static class RegisteredMachines
{
    /// <summary><paramref name="machines"/> with each type kept once, the first registration of it.</summary>
    public static IEnumerable<IMachine> Distinct(IEnumerable<IMachine> machines) =>
        machines.DistinctBy(m => m.GetType());
}
