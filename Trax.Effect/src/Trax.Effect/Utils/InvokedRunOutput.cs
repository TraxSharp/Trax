using System.Text.Json;
using System.Text.Json.Serialization;

namespace Trax.Effect.Utils;

/// <summary>
/// The copy of an invoked run's output that its machine's <c>OnDone</c> edges read: written on the run's own row,
/// in the run's terminal write, so a machine on any host can apply the outcome after any crash. It is not
/// <c>metadata.output</c>, which is redacted, bounded by host policy and may be absent; this one is the output as
/// the machine's guards and reductions see it, and it is never shown on an operator surface.
/// See <c>Trax.Docs/adr/0046-a-machine-state-invokes-a-train-and-only-that-entry-receives-its-outcome.md</c>.
/// </summary>
internal static class InvokedRunOutput
{
    /// <summary>
    /// The largest output stored, in UTF-8 bytes: the snapshot cap, since an output any larger could not be
    /// reduced into a snapshot that fits. A larger output is recorded as oversize instead, and its state fails.
    /// </summary>
    public const int MaxBytes = 64 * 1024;

    /// <summary>
    /// The naming the machine's declarative schema reflects (camelCase members, enums as camelCase strings), so
    /// the fields a guard or reduction names exist in the stored output. The engine serializes an outcome's
    /// output with these same options.
    /// </summary>
    public static JsonSerializerOptions Json { get; } =
        new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        };

    /// <summary>
    /// Serializes <paramref name="output"/> as <paramref name="declaredType"/>, the train's output type.
    /// </summary>
    /// <returns>
    /// The JSON and false; null and true when it is larger than <see cref="MaxBytes"/>. A value that cannot be
    /// serialized throws, as it would for the engine.
    /// </returns>
    public static (string? Json, bool Oversize) Serialize(object? output, Type declaredType)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(output, declaredType, Json);
        return bytes.Length > MaxBytes
            ? (null, true)
            : (System.Text.Encoding.UTF8.GetString(bytes), false);
    }
}
