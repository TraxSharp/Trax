using System.Text.Json;
using System.Text.Json.Nodes;
using Trax.Effect.Configuration.TraxEffectConfiguration;

namespace Trax.Effect.Services.Checkpoints;

/// <summary>
/// Whether a run's input is the input an earlier run stored: what a resume and a replay check
/// before they trust anything that run left behind. A run may carry on from another's checkpoint,
/// or take another's answers, only for the same input; anything else would hand it state or
/// decisions computed for a different request.
/// </summary>
internal static class RunInputs
{
    /// <summary>
    /// The input type <paramref name="train"/> declares, from the <c>ServiceTrain&lt;TIn, TOut&gt;</c>
    /// it derives from, or <see cref="object"/> when it derives from none.
    /// </summary>
    public static Type InputTypeOf(Type train)
    {
        for (var type = train; type is not null; type = type.BaseType)
            if (
                type.IsGenericType
                && type.GetGenericTypeDefinition() == typeof(ServiceTrain.ServiceTrain<,>)
            )
                return type.GetGenericArguments()[0];

        return typeof(object);
    }

    /// <summary>
    /// True when <paramref name="stored"/>, read back as <paramref name="inputType"/>, is the same
    /// value as <paramref name="input"/>. Both are compared as plain JSON trees, so the reference
    /// metadata a stored input carries and the order of its members do not count. A stored input
    /// that cannot be read back, or holds a placeholder or masked member in place of the value, is
    /// never the same. A stored input that is not JSON at all is the same only when it is exactly
    /// <paramref name="current"/>, the new run's own stored input.
    /// </summary>
    /// <remarks>
    /// A run that stored no input at all cannot be compared, and counts as the same. Its host
    /// keeps no train inputs (nothing registered <c>SaveTrainParameters</c>), and there nothing
    /// can requeue or resume a run by hand, since both read the stored input back: its links come
    /// only from a manifest's retry, which reads its input from the manifest.
    /// </remarks>
    public static bool Same(string? stored, string? current, object? input, Type inputType)
    {
        if (stored is null)
            return true;

        var system = TraxEffectConfiguration.StaticSystemJsonSerializerOptions;

        try
        {
            JsonDocument.Parse(stored).Dispose();
        }
        catch (JsonException)
        {
            return string.Equals(stored, current, StringComparison.Ordinal);
        }

        try
        {
            var read = new JsonSerializerOptions(system) { PropertyNameCaseInsensitive = true };
            var plain = new JsonSerializerOptions(system) { ReferenceHandler = null };
            var earlier = JsonSerializer.Deserialize(stored, inputType, read);

            return JsonNode.DeepEquals(
                JsonSerializer.SerializeToNode(earlier, inputType, plain),
                JsonSerializer.SerializeToNode(input, inputType, plain)
            );
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }
}
