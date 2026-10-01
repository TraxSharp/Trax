using System.Text.Json;
using Trax.Effect.Utils;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Api.GraphQL.Queries;

/// <summary>
/// Masks the <c>[TraxSensitive]</c> members of a train input that Trax keeps unmasked because it
/// runs from it: a work queue entry's input and a manifest's properties. The runner needs the
/// real values, so the stored copy keeps them; a read shows the copy the way an execution's
/// recorded input is shown, with each sensitive member written as <c>{"_redacted": true}</c>.
/// </summary>
/// <remarks>
/// The copy is read back as its input type with the options the job dispatcher reads it with,
/// then written with <see cref="TraxRedaction.WithRedaction"/>. When that cannot be done (the
/// type is not the input of a train registered on this host, or the JSON does not read as it),
/// nothing proves the copy holds no sensitive member, so the whole value is masked.
/// </remarks>
internal static class TransportInputRedaction
{
    /// <summary>What a value that could not be read as its type is shown as.</summary>
    internal static readonly string FullyMasked = $$"""{"{{TraxRedaction.MarkerProperty}}":true}""";

    /// <summary>
    /// The stored JSON with its sensitive members masked, or <c>null</c> when there is none.
    /// </summary>
    public static string? Redact(
        ITrainDiscoveryService discovery,
        string? json,
        string? inputTypeName
    )
    {
        if (json is null)
            return null;

        var inputType = FindInputType(discovery, inputTypeName);
        if (inputType is null)
            return FullyMasked;

        try
        {
            var options = TraxJsonSerializationOptions.ManifestProperties;
            var value = JsonSerializer.Deserialize(json, inputType, options);
            return value is null
                ? FullyMasked
                : JsonSerializer.Serialize(value, inputType, TraxRedaction.WithRedaction(options));
        }
        catch (Exception e) when (e is JsonException or NotSupportedException)
        {
            return FullyMasked;
        }
    }

    /// <summary>
    /// The registered train input type a stored type name names: its FullName, or an assembly
    /// qualified name whose assembly is that type's. Only registered inputs are considered, as the
    /// job dispatcher considers them, so a stored name never loads an arbitrary type.
    /// </summary>
    internal static Type? FindInputType(ITrainDiscoveryService discovery, string? typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return null;

        foreach (var registration in discovery.DiscoverTrains())
            if (Names(typeName, registration.InputType))
                return registration.InputType;

        return null;
    }

    private static bool Names(string typeName, Type type)
    {
        var fullName = type.FullName;
        if (fullName is null || !typeName.StartsWith(fullName, StringComparison.Ordinal))
            return false;

        if (typeName.Length == fullName.Length)
            return true;

        if (typeName[fullName.Length] != ',')
            return false;

        var rest = typeName.AsSpan(fullName.Length + 1).TrimStart();
        var comma = rest.IndexOf(',');
        var assemblyName = (comma < 0 ? rest : rest[..comma]).Trim();

        return assemblyName.Equals(type.Assembly.GetName().Name, StringComparison.Ordinal);
    }
}
