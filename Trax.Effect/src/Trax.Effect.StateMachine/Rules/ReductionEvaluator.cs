using System.Text.Json.Nodes;

namespace Trax.Effect.StateMachine;

/// <summary>
/// Applies a declarative <see cref="Reduction"/> to produce a transition's destination context. Total and
/// non-mutating: it always returns a fresh <see cref="JsonObject"/> and never aliases the inputs. A
/// <see cref="Reduction.Reset"/> needs the machine's initial context; a <see cref="Reduction.Custom"/>
/// resolves through a supplied handler map (an unregistered name carries the context forward, never throws).
/// </summary>
public static class ReductionEvaluator
{
    public static JsonObject Apply(
        Reduction reduction,
        JsonObject context,
        JsonNode? input,
        JsonObject initialContext,
        IReadOnlyDictionary<string, Func<JsonObject, JsonNode?, JsonObject>>? customReducers = null
    )
    {
        switch (reduction)
        {
            case Reduction.Keep:
                return (JsonObject)context.DeepClone();
            case Reduction.Clear:
                return new JsonObject();
            case Reduction.Reset:
                return (JsonObject)initialContext.DeepClone();
            case Reduction.Set set:
            {
                var next = (JsonObject)context.DeepClone();
                foreach (var step in set.Steps)
                    next[step.Field] = Resolve(step.Source, input);
                return next;
            }
            case Reduction.Custom custom:
                return
                    customReducers is not null
                    && customReducers.TryGetValue(custom.Name, out var reducer)
                    ? reducer(context, input)
                    : (JsonObject)context.DeepClone();
            default:
                return (JsonObject)context.DeepClone();
        }
    }

    private static JsonNode? Resolve(ValueSource source, JsonNode? input)
    {
        switch (source)
        {
            case ValueSource.Constant c:
                return c.Value?.DeepClone();
            case ValueSource.FromInput f:
                return input is JsonObject o && o.TryGetPropertyValue(f.Field, out var node)
                    ? node?.DeepClone()
                    : null;
            default:
                return null;
        }
    }
}
