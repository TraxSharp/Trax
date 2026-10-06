using System.Reflection;
using Trax.Core.Testing;

namespace Trax.Mediator.Testing;

/// <summary>
/// Architecture-guard checkers for trains. Reflection-based, matching the Trax base types by name so
/// no hard dependency on the train assemblies is needed beyond the ones the consumer passes in.
/// </summary>
public static class TrainGuards
{
    private const string ServiceTrainBaseName = "ServiceTrain`2";
    private const string ServiceTrainInterfaceName = "IServiceTrain`2";

    /// <summary>
    /// Every concrete train in the given assemblies must have its own train interface: exactly one
    /// most-derived non-generic interface deriving <c>IServiceTrain&lt;TIn, TOut&gt;</c>. That
    /// interface's FullName is the train's canonical identity throughout Trax, and it is the rule
    /// the registry applies when it scans, so a train this guard passes registers under its own
    /// name and one it flags does not.
    /// </summary>
    /// <remarks>
    /// A train is a concrete class that derives from <c>ServiceTrain&lt;,&gt;</c> or implements
    /// <c>IServiceTrain&lt;,&gt;</c> directly. The interface is conventionally named
    /// <c>I{Name}</c>, but any name is accepted, as it is at runtime. A train with no such
    /// interface is registered only under the shared <c>IServiceTrain&lt;TIn, TOut&gt;</c>, and
    /// one with two that neither extends is refused when scanned.
    /// </remarks>
    public static GuardResult EveryTrainHasInterface(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        var offenders = new List<string>();
        var inspected = 0;

        foreach (var train in assemblies.SelectMany(GetLoadableTypes).Where(IsConcreteTrain))
        {
            inspected++;

            var candidates = train
                .GetInterfaces()
                .Where(i => !i.IsGenericType && i.GetInterfaces().Any(IsServiceTrainInterface))
                .ToList();

            var mostDerived = candidates
                .Where(c => !candidates.Any(other => other != c && c.IsAssignableFrom(other)))
                .ToList();

            if (mostDerived.Count == 0)
                offenders.Add(
                    $"{train.FullName} (no interface deriving IServiceTrain<,>; expected one such "
                        + $"as I{train.Name})"
                );
            else if (mostDerived.Count > 1)
                offenders.Add(
                    $"{train.FullName} (implements {mostDerived.Count} train interfaces, none "
                        + "extending the others: "
                        + string.Join(", ", mostDerived.Select(i => i.FullName))
                        + ")"
                );
        }

        var message =
            "Every train needs exactly one interface of its own deriving IServiceTrain<TIn, TOut>, "
            + "conventionally I{Name}. Offenders:\n  "
            + string.Join("\n  ", offenders);

        return new GuardResult(offenders, inspected, message);
    }

    private static bool IsConcreteTrain(Type type)
    {
        if (type is not { IsAbstract: false, IsClass: true } || type.ContainsGenericParameters)
            return false;

        for (var t = type.BaseType; t is not null; t = t.BaseType)
        {
            if (t.IsGenericType && t.Name == ServiceTrainBaseName)
                return true;
        }

        return type.GetInterfaces().Any(IsServiceTrainInterface);
    }

    private static bool IsServiceTrainInterface(Type type) =>
        type.IsGenericType && type.Name == ServiceTrainInterfaceName;

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }
}
