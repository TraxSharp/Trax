using Microsoft.Extensions.Hosting;
using Trax.Effect.Configuration.ClientExport;

namespace Trax.StateMachine.Persistence;

/// <summary>The registered machines, each type once: the same type registered twice is one machine.</summary>
internal static class RegisteredMachines
{
    /// <summary><paramref name="machines"/> with each type kept once, the first registration of it.</summary>
    public static IEnumerable<IMachine> Distinct(IEnumerable<IMachine> machines) =>
        machines.DistinctBy(m => m.GetType());
}
