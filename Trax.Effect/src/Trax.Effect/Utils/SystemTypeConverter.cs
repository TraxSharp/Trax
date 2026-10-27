using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Trax.Effect.Utils;

/// <summary>
/// Provides JSON serialization and deserialization support for System.Type objects.
/// </summary>
/// <remarks>
/// The SystemTypeConverter is a custom JSON converter that enables System.Type objects
/// to be properly serialized to and deserialized from JSON. This is necessary because
/// the default System.Text.Json serialization does not natively support Type objects.
///
/// This converter serializes Type objects as their assembly-qualified name strings,
/// which uniquely identify a type across different assemblies and can be used to
/// recreate the Type object during deserialization.
///
/// This converter is particularly useful in the Trax.Effect system because:
/// 1. Type information is often needed for reflection-based operations
/// 2. Types need to be persisted for later use or analysis
/// 3. Type information is used for dynamic method invocation and object creation
///
/// The converter is registered in the TraxJsonSerializationOptions.Default
/// options, making it available throughout the system.
/// </remarks>
internal class SystemTypeConverter : JsonConverter<Type>
{
    /// <summary>
    /// Reads a JSON value and converts it to a Type object.
    /// </summary>
    /// <param name="reader">The JSON reader</param>
    /// <param name="typeToConvert">The type to convert to (always System.Type)</param>
    /// <param name="options">The serializer options</param>
    /// <returns>The deserialized Type object, or null if the input is null</returns>
    /// <exception cref="JsonException">Thrown if no loaded assembly defines the type</exception>
    /// <remarks>
    /// This method:
    /// 1. Reads the assembly-qualified name string from the JSON
    /// 2. Resolves it among the assemblies already loaded (<see cref="LoadedTypeResolver"/>), the
    ///    type, its generic arguments and an array's element alike; no assembly is loaded by name
    /// 3. Throws an exception if the type cannot be found there
    ///
    /// The assembly-qualified name format includes the type's full name, assembly name,
    /// version, culture, and public key token, which uniquely identifies the type
    /// across different assemblies.
    ///
    /// For example: "System.String, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089"
    /// </remarks>
    public override Type? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        var assemblyQualifiedName = reader.GetString();

        if (assemblyQualifiedName == null)
            return null;

        var resultType = LoadedTypeResolver.Resolve(
            assemblyQualifiedName,
            allowAssemblyQualified: true
        );

        if (resultType == null)
            throw new JsonException($"Unable to find type: {assemblyQualifiedName}");

        return resultType;
    }

    /// <summary>
    /// Writes a Type object as JSON.
    /// </summary>
    /// <param name="writer">The JSON writer</param>
    /// <param name="value">The Type object to write</param>
    /// <param name="options">The serializer options</param>
    /// <remarks>
    /// This method serializes a Type object as its assembly-qualified name string.
    /// The assembly-qualified name uniquely identifies the type and can be used
    /// to recreate the Type object during deserialization.
    ///
    /// If the value is null, a null JSON value is written.
    ///
    /// The assembly-qualified name format includes the type's full name, assembly name,
    /// version, culture, and public key token, which uniquely identifies the type
    /// across different assemblies.
    ///
    /// For example: "System.String, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089"
    /// </remarks>
    public override void Write(Utf8JsonWriter writer, Type value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value?.AssemblyQualifiedName);
    }
}
