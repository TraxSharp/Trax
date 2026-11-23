using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Trax.StateMachine;

/// <summary>
/// What a machine replayed from its IR declares a trigger's input with: the record the API is typed from, and the IR's
/// own schema. A hand-written machine reflects one from the other with
/// <see cref="ITransitionBuilder{TState,TTrigger}.WithInput{TInput}"/>.
/// </summary>
internal interface IIrTransitionBuilder<TState, TTrigger>
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    ITransitionBuilder<TState, TTrigger> WithInput(Type record, ContextSchema schema);
}
