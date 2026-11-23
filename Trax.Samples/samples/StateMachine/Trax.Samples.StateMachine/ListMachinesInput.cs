using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine.Persistence;

namespace Trax.Samples.StateMachine;

// A read that lists the registered machines. Beyond being a handy "what can I drive?" query, it gives the
// GraphQL schema a root Query field beside the machines' own operations, and HotChocolate requires a
// non-empty Query type.

public record ListMachinesInput;
