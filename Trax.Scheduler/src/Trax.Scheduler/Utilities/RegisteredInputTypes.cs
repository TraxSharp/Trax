using Trax.Mediator.Services.TrainRegistry;

namespace Trax.Scheduler.Utilities;

/// <summary>
/// Finds an input type by name among the registered trains' input types. Every name the
/// scheduler or a runner turns back into a type goes through here: a runner request's, a work
/// queue row's and a background job's (see scheduler/0006). The name is only ever compared,
/// never loaded, so a name that is not a registered train's input resolves to nothing.
/// </summary>
internal static class RegisteredInputTypes
{
    /// <summary>
    /// The registered input type <paramref name="typeName"/> names, or null. Accepts the type's
    /// full name, which is what Trax writes, and its assembly-qualified name, provided the
    /// assembly named is the one the registered type lives in; the assembly's version, culture
    /// and key are not compared, so a row written before an upgrade still resolves.
    /// </summary>
    public static Type? Find(ITrainRegistry registry, string typeName)
    {
        foreach (var inputType in registry.InputTypeToTrain.Keys)
            if (Names(typeName, inputType))
                return inputType;

        return null;
    }

    private static bool Names(string typeName, Type type)
    {
        var fullName = type.FullName;
        if (fullName is null || !typeName.StartsWith(fullName, StringComparison.Ordinal))
            return false;

        if (typeName.Length == fullName.Length)
            return true;

        // "<FullName>, <Assembly>[, Version=..., ...]"
        if (typeName[fullName.Length] != ',')
            return false;

        var rest = typeName.AsSpan(fullName.Length + 1).TrimStart();
        var comma = rest.IndexOf(',');
        var assemblyName = (comma < 0 ? rest : rest[..comma]).Trim();

        return assemblyName.Equals(type.Assembly.GetName().Name, StringComparison.Ordinal);
    }
}
