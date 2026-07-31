using LanguageExt;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Samples.StateMachine;

// A read that lists the registered machines. Beyond being a handy "what can I drive?" query, it gives the
// GraphQL schema a root Query field: the four stateMachine operations are all mutations, and HotChocolate
// requires a non-empty Query type.

public record ListMachinesInput;

public record MachineInfo
{
    public required string Name { get; init; }
    public required bool HasEffect { get; init; }
}

public record ListMachinesOutput
{
    public required IReadOnlyList<MachineInfo> Machines { get; init; }
}

public interface IListMachines : IServiceTrain<ListMachinesInput, ListMachinesOutput> { }

[TraxAllowAnonymous]
[TraxQuery(Namespace = "stateMachine", Description = "List the registered state machines.")]
public class ListMachines : ServiceTrain<ListMachinesInput, ListMachinesOutput>, IListMachines
{
    protected override Task<Either<Exception, ListMachinesOutput>> Junctions() =>
        Chain<ListMachinesJunction>().Resolve();
}

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
