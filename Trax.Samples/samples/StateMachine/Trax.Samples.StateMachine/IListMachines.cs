using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine.Persistence;

namespace Trax.Samples.StateMachine;

public interface IListMachines : IServiceTrain<ListMachinesInput, ListMachinesOutput>;
