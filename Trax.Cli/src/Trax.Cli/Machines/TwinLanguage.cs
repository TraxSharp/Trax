using System.Text.Json;
using System.Text.Json.Serialization;

namespace Trax.Cli.Machines;

/// <summary>A language <c>--lang</c> takes, and what <c>--twin-out</c> writes in it.</summary>
internal sealed record TwinLanguage(
    [property: JsonPropertyName("lang")] string Lang,
    [property: JsonPropertyName("writes")] string Writes
);
