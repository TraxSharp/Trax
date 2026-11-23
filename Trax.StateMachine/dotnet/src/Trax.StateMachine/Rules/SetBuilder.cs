using System.Linq.Expressions;
using System.Text.Json.Nodes;

namespace Trax.StateMachine;

/// <summary>Completes a <see cref="Rules.Set{TContext,TField}"/>: where the field's new value comes from.</summary>
public sealed class SetBuilder(string field)
{
    /// <summary>Set the field from a trigger-input field.</summary>
    public Reduction FromInput<TInput, TField>(Expression<Func<TInput, TField>> input) =>
        new Reduction.Set([new SetStep(field, new ValueSource.FromInput(MemberPath.Of(input)))]);

    /// <summary>Set the field to a literal value.</summary>
    public Reduction ToValue(JsonNode? value) =>
        new Reduction.Set([new SetStep(field, new ValueSource.Constant(value))]);
}
