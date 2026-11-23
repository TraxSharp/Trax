using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using HotChocolate.Types;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Api.GraphQL.TypeModules;

/// <summary>
/// The other surfaces of the schema a machine enum could also reach (each exposed train's input and output, each query
/// model's entity), and the enums the host accepted the machine's spelling for.
/// </summary>
/// <param name="Surfaces">How a refusal names the surface, and the CLR type it starts from.</param>
/// <param name="Accepted">The enums <c>ShareEnumWithMachines</c> named.</param>
internal sealed record MachineEnumNeighbours(
    IReadOnlyList<(string Surface, Type Root)> Surfaces,
    IReadOnlySet<Type> Accepted
);
