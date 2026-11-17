using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Trax.Client.Checks;

namespace Trax.Client;

/// <summary>
/// The converter <see cref="Optional{T}"/> names in its <c>[JsonConverter]</c>. Public so a source-generated
/// <see cref="JsonSerializerContext"/> in another assembly can create it; nothing calls it directly.
/// </summary>
public sealed class OptionalJsonConverterFactory : JsonConverterFactory
{
    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType
        && typeToConvert.GetGenericTypeDefinition() == typeof(Optional<>);

    /// <inheritdoc/>
    public override JsonConverter CreateConverter(
        Type typeToConvert,
        JsonSerializerOptions options
    ) => DefaultOf(typeToConvert).Converter();

    // A default Optional<T> asks for its own converter, so no generic type is built at run time.
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2067",
        Justification = "Optional<T> is a struct, whose default value needs no constructor the trimmer could remove."
    )]
    private static IOptional DefaultOf(Type optional) =>
        (IOptional)Activator.CreateInstance(optional).NotNull();
}
