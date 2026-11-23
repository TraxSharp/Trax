using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine.Persistence;

namespace Trax.Samples.StateMachine;

public class ListMachinesJunction(IEnumerable<IMachine> machines)
    : Junction<ListMachinesInput, ListMachinesOutput>
{
    public override Task<ListMachinesOutput> Run(ListMachinesInput input) =>
        Task.FromResult(
            new ListMachinesOutput
            {
                Machines = machines
                    .Select(m => new MachineInfo { Name = m.Name, HasEffect = m.HasEffect })
                    .OrderBy(m => m.Name, StringComparer.Ordinal)
                    .ToList(),
            }
        );
}
