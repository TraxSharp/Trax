using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using Trax.Core.Exceptions;
using Trax.Effect.Utils;

namespace Trax.Scheduler.Services.RunExecutor;

/// <summary>
/// Reads a remote run's output on the calling side, shared by the HTTP and Lambda run executors.
/// </summary>
/// <remarks>
/// A concrete declared output is read into itself, whatever type the response names. An interface
/// or abstract declared output cannot be read into, so the response's <c>OutputType</c> picks the
/// implementation, but only from the concrete types already loaded in this process that implement
/// the declared type, matched by name. Nothing is loaded by the name a response gives, and a name
/// that matches no such implementation is refused (see scheduler/0006).
/// <para>
/// Public because Trax.Scheduler.Lambda ships as a separate package and calls it. A package that
/// reached it through InternalsVisibleTo would break with MissingMethodException the first time a
/// consumer pulled in a newer Trax.Scheduler than it was built against. It is hidden from
/// IntelliSense because it is plumbing between the Trax packages, not something to call.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class RemoteRunOutput
{
    private static readonly ConcurrentDictionary<Type, Implementations> Cache = new();

    /// <summary>
    /// Deserializes <paramref name="outputJson"/> into <paramref name="declaredOutput"/>, or into
    /// the loaded implementation of it that <paramref name="namedType"/> names.
    /// </summary>
    /// <exception cref="TrainException">
    /// The declared output is an interface or abstract type and <paramref name="namedType"/> names
    /// no loaded implementation of it.
    /// </exception>
    public static object? Read(string? outputJson, Type declaredOutput, string? namedType)
    {
        if (outputJson is null)
            return null;

        return JsonSerializer.Deserialize(
            outputJson,
            ReadAs(declaredOutput, namedType),
            TraxJsonSerializationOptions.ManifestProperties
        );
    }

    internal static Type ReadAs(Type declaredOutput, string? namedType)
    {
        if (!declaredOutput.IsInterface && !declaredOutput.IsAbstract)
            return declaredOutput;

        if (namedType is not null && Lookup(declaredOutput, namedType) is { } implementation)
            return implementation;

        throw new TrainException(
            $"The remote run returned output of type '{namedType ?? "(none)"}', which is not a "
                + $"loaded implementation of the declared output type '{declaredOutput.FullName}'."
        );
    }

    private static Type? Lookup(Type declaredOutput, string namedType)
    {
        var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies().Length;
        var implementations = Cache.GetOrAdd(declaredOutput, Scan);

        if (implementations.Find(namedType, out var found))
            return found;

        // An assembly loaded since the scan may hold it. Rescan only when that is possible, so a
        // name that matches nothing does not cost a scan on every run.
        if (implementations.AssemblyCount == loadedAssemblies)
            return null;

        implementations = Scan(declaredOutput);
        Cache[declaredOutput] = implementations;
        return implementations.Find(namedType, out found) ? found : null;
    }

    private static Implementations Scan(Type declaredOutput)
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        var byName = new Dictionary<string, Type?>(StringComparer.Ordinal);

        foreach (var type in assemblies.Where(a => !a.IsDynamic).SelectMany(LoadableTypes))
        {
            if (
                type.IsAbstract
                || type.IsInterface
                || type.ContainsGenericParameters
                || !declaredOutput.IsAssignableFrom(type)
            )
                continue;

            foreach (var name in new[] { type.FullName, type.AssemblyQualifiedName })
                if (name is not null)
                    // Two loaded implementations sharing a name are ambiguous, so neither is read.
                    byName[name] = byName.ContainsKey(name) ? null : type;
        }

        return new Implementations(byName, assemblies.Length);
    }

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>();
        }
    }

    private sealed record Implementations(Dictionary<string, Type?> ByName, int AssemblyCount)
    {
        public bool Find(string name, out Type? type) =>
            ByName.TryGetValue(name, out type) && type is not null;
    }
}
