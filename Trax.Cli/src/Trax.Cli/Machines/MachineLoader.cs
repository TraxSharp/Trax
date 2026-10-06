using System.Reflection;
using System.Runtime.Loader;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Cli.Machines;

/// <summary>
/// Loads a compiled machine assembly and hands back the <see cref="IMachine"/> to export. The assembly goes into
/// the default <see cref="AssemblyLoadContext"/>, so its reference to <c>Trax.Effect.StateMachine.Persistence</c>
/// binds by simple name to the copy the CLI already has: type identity is preserved, and a machine built against
/// an older engine still unifies with the CLI's own <see cref="IMachine"/>. A machine built against a newer one
/// cannot: the default context already holds that simple name at a lower version. How that surfaces has not been
/// observed. The consumer's non-Trax dependencies resolve from the assembly's own directory.
/// </summary>
internal static class MachineLoader
{
    private static bool _resolverHooked;

    /// <summary>Every non-abstract <see cref="IMachine"/> with a public parameterless constructor.</summary>
    public static IReadOnlyList<Type> DiscoverMachines(Assembly assembly) =>
        DiscoverMachines(assembly, out _);

    /// <summary>
    /// Every non-abstract <see cref="IMachine"/> with a public parameterless constructor, among the types that
    /// load. A type that cannot load (one deriving from a shared framework the CLI does not carry, such as
    /// ASP.NET Core, or from a dependency that is missing) is skipped rather than hiding every machine beside
    /// it; what the loader threw for those is returned so a caller that finds no machine can say why.
    /// </summary>
    internal static IReadOnlyList<Type> DiscoverMachines(
        Assembly assembly,
        out IReadOnlyList<Exception> loaderExceptions
    )
    {
        Type[] types;
        try
        {
            types = assembly.GetTypes();
            loaderExceptions = [];
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.OfType<Type>().ToArray();
            loaderExceptions = ex.LoaderExceptions.OfType<Exception>().ToList();
        }

        return types
            .Where(t =>
                typeof(IMachine).IsAssignableFrom(t)
                && t is { IsAbstract: false, IsInterface: false }
                && t.GetConstructor(Type.EmptyTypes) is not null
            )
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Load the assembly, pick the machine (by <paramref name="machineFullName"/>, or the only one present),
    /// and instantiate it. Throws <see cref="InvalidOperationException"/> with an actionable message when the
    /// assembly is missing, has no machines, has several and none was named, or the name does not match.
    /// </summary>
    public static IMachine Load(string assemblyPath, string? machineFullName)
    {
        var fullPath = Path.GetFullPath(assemblyPath);
        if (!File.Exists(fullPath))
            throw new InvalidOperationException($"Assembly not found: {fullPath}");

        HookDependencyResolver(fullPath);

        Assembly assembly;
        try
        {
            assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath);
        }
        catch (Exception ex) when (ex is BadImageFormatException or FileLoadException)
        {
            throw new InvalidOperationException(
                $"Could not load '{fullPath}' as a .NET assembly: {ex.Message}",
                ex
            );
        }

        var machines = DiscoverMachines(assembly, out var loaderExceptions);
        if (machines.Count == 0)
            throw new InvalidOperationException(
                $"No machines found in {Path.GetFileName(fullPath)}. A machine is a non-abstract "
                    + "IMachine (a Machine<TState, TTrigger> subclass) with a parameterless constructor. "
                    + "If the assembly references a different Trax.Effect.StateMachine version than this CLI, "
                    + "its machines will not be recognized; rebuild it against the matching version."
                    + DescribeLoaderExceptions(loaderExceptions)
            );

        var type = SelectMachine(machines, machineFullName, Path.GetFileName(fullPath));
        return (IMachine)Activator.CreateInstance(type)!;
    }

    // The first few distinct reasons some types did not load, so the user sees which dependency is missing.
    private static string DescribeLoaderExceptions(IReadOnlyList<Exception> loaderExceptions) =>
        loaderExceptions.Count == 0
            ? ""
            : "\nSome types in the assembly could not be loaded:\n  "
                + string.Join(
                    "\n  ",
                    loaderExceptions.Select(e => e.Message).Distinct(StringComparer.Ordinal).Take(3)
                );

    internal static Type SelectMachine(
        IReadOnlyList<Type> machines,
        string? machineFullName,
        string assemblyName
    )
    {
        if (machineFullName is not null)
            return machines.FirstOrDefault(t => t.FullName == machineFullName)
                ?? throw new InvalidOperationException(
                    $"Machine '{machineFullName}' not found in {assemblyName}. Available:\n  "
                        + string.Join("\n  ", machines.Select(t => t.FullName))
                );

        if (machines.Count == 1)
            return machines[0];

        throw new InvalidOperationException(
            $"{assemblyName} contains {machines.Count} machines; pass --machine <FullName> to pick one:\n  "
                + string.Join("\n  ", machines.Select(t => t.FullName))
        );
    }

    // Resolve the consumer's own dependencies from its output directory. Trax.Effect.* are already loaded (the
    // CLI references them), so the loader never asks for them here and their type identity is preserved.
    private static void HookDependencyResolver(string assemblyPath)
    {
        if (_resolverHooked)
            return;
        _resolverHooked = true;

        var resolver = new AssemblyDependencyResolver(assemblyPath);
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            var path = resolver.ResolveAssemblyToPath(name);
            return path is null ? null : context.LoadFromAssemblyPath(path);
        };
    }
}
