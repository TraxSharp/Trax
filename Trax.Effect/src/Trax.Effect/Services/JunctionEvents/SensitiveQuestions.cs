using System.Collections.Concurrent;
using System.Reflection;
using Trax.Core.Decisions;
using Trax.Effect.Attributes;

namespace Trax.Effect.Services.JunctionEvents;

/// <summary>
/// Decides whose answers junction events and <c>trax.junction_run</c> leave out: a question about
/// an enum or marker type marked <see cref="TraxSensitiveAttribute"/>, directly, through a base
/// type, or anywhere in the type's name (a closed form of a marked generic type, a type nested in
/// one, or a type that takes a marked one as an argument).
/// </summary>
/// <remarks>
/// <para>A decision reaches the observer as a key, not a type, so the marked types are found by
/// scanning the loaded assemblies that reference Trax.Effect (the only ones that can carry the
/// attribute) once, and each assembly loaded afterwards as it loads. Each marked type contributes
/// the name its keys are built from: its <c>[Asks(Key = ...)]</c> when it declares one, and its name
/// without namespace or generic arity. A key is sensitive when it is one of those names, or when any
/// name it is built from is: <c>QuestionKey.For</c> writes a closed generic as
/// <c>Flag&lt;Refund&gt;</c>, a nested type as <c>Outer&lt;X&gt;.Flag</c>, an array as
/// <c>Flag[]</c>, and each part is checked.</para>
///
/// <para>This fails closed. Two types can share a name (<c>QuestionKey.For</c> drops the
/// namespace), and when either is marked the answer is withheld for both.</para>
/// </remarks>
internal static class SensitiveQuestions
{
    private static readonly string EffectAssembly = typeof(TraxSensitiveAttribute)
        .Assembly.GetName()
        .Name!;

    private static readonly char[] KeySeparators = ['<', '>', ',', '[', ']'];

    private static readonly ConcurrentDictionary<string, byte> Names = new(StringComparer.Ordinal);

    private static readonly Lazy<bool> Scanned = new(ScanLoaded, isThreadSafe: true);

    /// <summary>Whether the answer to the question with this key must be withheld.</summary>
    public static bool IsSensitive(string key)
    {
        _ = Scanned.Value;

        if (Names.ContainsKey(key))
            return true;

        foreach (var part in key.Split(KeySeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (Names.ContainsKey(part))
                return true;

            foreach (var segment in part.Split('.', StringSplitOptions.RemoveEmptyEntries))
                if (Names.ContainsKey(segment))
                    return true;
        }

        return false;
    }

    /// <summary>
    /// Whether the answer to a question must be withheld, from the type it is about when Trax.Core
    /// reported one, and from its key either way, so a type the scan never saw still fails closed.
    /// </summary>
    public static bool IsSensitive(Type? type, string key) =>
        type is not null ? IsSensitive(type) || IsSensitive(key) : IsSensitive(key);

    /// <summary>Whether the answer to a question about <paramref name="type"/> must be withheld.</summary>
    public static bool IsSensitive(Type type) =>
        IsMarked(type) || IsSensitive(QuestionKey.For(type));

    /// <summary>
    /// Whether the type, its generic definition, a type it is nested in, its element type or any
    /// of its generic arguments carries the mark, its own or a base type's.
    /// </summary>
    private static bool IsMarked(Type type)
    {
        if (type.HasElementType && type.GetElementType() is { } element && IsMarked(element))
            return true;

        if (type.IsGenericParameter)
            return false;

        if (type.IsDefined(typeof(TraxSensitiveAttribute), inherit: true))
            return true;

        if (
            type.IsGenericType
            && !type.IsGenericTypeDefinition
            && (
                type.GetGenericTypeDefinition().IsDefined(typeof(TraxSensitiveAttribute), true)
                || type.GetGenericArguments().Any(IsMarked)
            )
        )
            return true;

        return type.DeclaringType is { } outer && IsMarked(outer);
    }

    /// <summary>Records <paramref name="type"/> when it is marked. For tests and the scan.</summary>
    internal static void Register(Type type)
    {
        if (!type.IsDefined(typeof(TraxSensitiveAttribute), inherit: true))
            return;

        if (type.GetCustomAttribute<AsksAttribute>(inherit: false)?.Key is { } declared)
            Names.TryAdd(declared, 0);

        var name = type.Name;
        var tick = name.IndexOf('`');
        Names.TryAdd(tick >= 0 ? name[..tick] : name, 0);
    }

    private static bool ScanLoaded()
    {
        AppDomain.CurrentDomain.AssemblyLoad += (_, args) => Scan(args.LoadedAssembly);

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            Scan(assembly);

        return true;
    }

    private static void Scan(Assembly assembly)
    {
        Type?[] types;

        try
        {
            if (assembly.IsDynamic)
                return;

            var name = assembly.GetName().Name;
            if (
                name != EffectAssembly
                && !assembly.GetReferencedAssemblies().Any(r => r.Name == EffectAssembly)
            )
                return;

            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException partial)
            {
                types = partial.Types;
            }
        }
        catch
        {
            // An assembly that cannot be inspected cannot carry a mark this process could read.
            return;
        }

        // One type that cannot be read does not hide the marks on the others.
        foreach (var type in types)
        {
            if (type is null)
                continue;

            try
            {
                Register(type);
            }
            catch
            {
                // Its attributes could not be read, so neither could a mark on it.
            }
        }
    }
}
