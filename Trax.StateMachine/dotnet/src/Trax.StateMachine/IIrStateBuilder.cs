using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Trax.StateMachine;

/// <summary>
/// What a machine replayed from its IR (<c>Trax.StateMachine.Persistence</c>, ADR <c>Trax.Docs/adr/0058</c>) declares a
/// state's context with: the record the API is typed from, and the IR's own schema, which is what the engines check.
/// A hand-written machine reflects one from the other with <see cref="IStateBuilder{TState,TTrigger}.Context{TContext}"/>.
/// </summary>
internal interface IIrStateBuilder<TState, TTrigger>
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    IStateBuilder<TState, TTrigger> Context(Type record, ContextSchema schema);

    /// <summary>
    /// <see cref="IStateBuilder{TState,TTrigger}.Invokes{TTrain,TInput,TOutput}"/> with an input built from the
    /// context and the instance owner's user key (null for a system instance), which the host file's <c>$owner</c>
    /// maps a train field to. The owner is the server's to give: no client writes it.
    /// </summary>
    IInvokeBuilder<TState, TTrigger> InvokesAsOwner<TTrain, TInput, TOutput>(
        Func<JsonObject, string?, TInput> input
    )
        where TTrain : Trax.Effect.Services.ServiceTrain.IServiceTrain<TInput, TOutput>;
}
