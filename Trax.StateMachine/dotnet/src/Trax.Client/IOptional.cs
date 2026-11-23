using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Trax.Client;

/// <summary>What <see cref="TraxJson"/> reads of any <see cref="Optional{T}"/>.</summary>
internal interface IOptional
{
    bool HasValue { get; }
    object? Boxed { get; }

    /// <summary>The converter for this Optional's type, so the factory needs no MakeGenericType.</summary>
    JsonConverter Converter();
}
