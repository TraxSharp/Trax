using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Trax.Effect.Utils;

/// <summary>
/// Provides JSON serialization and deserialization support for ValueTuple types.
/// </summary>
/// <remarks>
/// The ValueTupleConverter is a custom JSON converter that enables System.ValueTuple types
/// to be properly serialized to and deserialized from JSON. This is necessary because
/// the default System.Text.Json serialization does not natively support ValueTuple types.
///
/// This converter handles ValueTuple types with up to 7 elements (the maximum supported
/// by the ValueTuple struct). It serializes ValueTuples as JSON arrays and deserializes
/// JSON arrays back into ValueTuples, reading each element as its field's type.
///
/// This converter is particularly useful in the Trax.Effect system because:
/// 1. ValueTuples are used extensively for returning multiple values from methods
/// 2. These tuples often need to be serialized for persistence or transmission
/// 3. The serialized form needs to be human-readable and compact
///
/// The converter is registered in the TraxJsonSerializationOptions.Default
/// options, making it available throughout the system.
/// </remarks>
internal class ValueTupleConverter : JsonConverterFactory
{
    /// <summary>
    /// Determines whether the converter can convert the specified type.
    /// </summary>
    /// <param name="typeToConvert">The type to check</param>
    /// <returns>True if the type is a ValueTuple; otherwise, false</returns>
    /// <remarks>
    /// This method checks if the type:
    /// 1. Is a value type (struct)
    /// 2. Is a generic type
    /// 3. Has a full name that starts with "System.ValueTuple"
    ///
    /// These conditions identify all ValueTuple types, such as:
    /// - ValueTuple&lt;T1, T2&gt;
    /// - ValueTuple&lt;T1, T2, T3&gt;
    /// - etc.
    /// </remarks>
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert is { IsValueType: true, IsGenericType: true }
        && typeToConvert.FullName!.StartsWith("System.ValueTuple");

    /// <summary>
    /// Creates a converter for the specified type.
    /// </summary>
    /// <param name="type">The type to create a converter for</param>
    /// <param name="options">The serializer options to use</param>
    /// <returns>A converter for the specified type</returns>
    /// <remarks>
    /// This method creates an instance of ValueTupleConverterInner&lt;T&gt;
    /// for the specific ValueTuple type being converted. It uses reflection
    /// to create a generic type instance at runtime.
    ///
    /// The inner converter class handles the actual serialization and
    /// deserialization logic for the specific ValueTuple type.
    /// </remarks>
    public override JsonConverter? CreateConverter(Type type, JsonSerializerOptions options)
    {
        Type converterType = typeof(ValueTupleConverterInner<>).MakeGenericType(type);
        return (JsonConverter?)Activator.CreateInstance(converterType);
    }

    /// <summary>
    /// Inner converter class that handles the actual serialization and deserialization
    /// of a specific ValueTuple type.
    /// </summary>
    /// <typeparam name="T">The ValueTuple type to convert</typeparam>
    /// <remarks>
    /// This inner class is generic over the specific ValueTuple type being converted.
    /// It uses reflection to access the fields of the ValueTuple and to create new
    /// instances during deserialization.
    ///
    /// The converter serializes ValueTuples as JSON arrays and deserializes JSON arrays
    /// back into ValueTuples, deserializing each element as its field's type.
    /// </remarks>
    private class ValueTupleConverterInner<T> : JsonConverter<T>
        where T : struct
    {
        /// <summary>
        /// The fields of the ValueTuple type, obtained through reflection.
        /// </summary>
        /// <remarks>
        /// ValueTuple types have fields named Item1, Item2, etc., which contain
        /// the actual values of the tuple elements. This array provides access
        /// to those fields for serialization and deserialization.
        /// </remarks>
        private static readonly FieldInfo[] Fields = typeof(T).GetFields();

        /// <summary>
        /// Reads a JSON value and converts it to a ValueTuple.
        /// </summary>
        /// <param name="reader">The JSON reader</param>
        /// <param name="typeToConvert">The type to convert to</param>
        /// <param name="options">The serializer options</param>
        /// <returns>The deserialized ValueTuple</returns>
        /// <exception cref="JsonException">Thrown if the value is not an array or if the array length doesn't match the tuple size</exception>
        /// <remarks>
        /// Each element is deserialized as its own field's type, so a number becomes the
        /// field's numeric type and an object becomes the field's class. The array may be
        /// bare, or inside the <c>{"$id": …, "$values": […]}</c> wrapper that
        /// <see cref="Write"/> produces when the options preserve references.
        /// </remarks>
        public override T Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        )
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var array = document.RootElement;

            if (
                array.ValueKind == JsonValueKind.Object
                && array.TryGetProperty("$values", out var values)
            )
                array = values;

            if (array.ValueKind != JsonValueKind.Array)
                throw new JsonException(
                    $"Expected a JSON array for {typeof(T)}, but got {array.ValueKind}"
                );

            var length = array.GetArrayLength();
            if (length != Fields.Length)
                throw new JsonException($"Expected {Fields.Length} values, but got {length}");

            var args = array
                .EnumerateArray()
                .Zip(Fields, (element, field) => element.Deserialize(field.FieldType, options))
                .ToArray();
            return (T)Activator.CreateInstance(typeof(T), args)!;
        }

        /// <summary>
        /// Writes a ValueTuple as JSON.
        /// </summary>
        /// <param name="writer">The JSON writer</param>
        /// <param name="value">The ValueTuple to write</param>
        /// <param name="options">The serializer options</param>
        /// <remarks>
        /// Writes a JSON array with one element per field. Each element is serialized on its own,
        /// as <see cref="Read"/> reads each one on its own: under
        /// <see cref="ReferenceHandler.Preserve"/>, one call over all the elements wrote an object
        /// the tuple holds twice as a <c>$ref</c> the second time, and reading that element alone
        /// could not resolve it. Each element is now written in full, so a shared object appears
        /// once per element that holds it.
        /// </remarks>
        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();

            foreach (var field in Fields)
                JsonSerializer.Serialize(writer, field.GetValue(value), field.FieldType, options);

            writer.WriteEndArray();
        }
    }
}
