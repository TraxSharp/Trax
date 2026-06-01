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
    /// Every concrete <c>ServiceTrain&lt;TIn, TOut&gt;</c> in the given assemblies must implement a
    /// companion <c>I{Name}</c> interface deriving <c>IServiceTrain&lt;TIn, TOut&gt;</c> (the interface
    /// FullName is the canonical train identity throughout Trax).
    /// </summary>
    public static GuardResult EveryTrainHasInterface(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        var offenders = new List<string>();
        var inspected = 0;

        foreach (var train in assemblies.SelectMany(GetLoadableTypes).Where(IsConcreteServiceTrain))
        {
            inspected++;
            var expected = "I" + train.Name;
            var marker = train
                .GetInterfaces()
                .FirstOrDefault(i =>
                    i.Name == expected
                    && i.GetInterfaces().Any(b => b.Name == ServiceTrainInterfaceName)
                );

            if (marker is null)
                offenders.Add(
                    $"{train.FullName} (expected interface {expected} : IServiceTrain<,>)"
                );
        }

        var message =
            "Every train needs a companion I{Name} interface deriving IServiceTrain<TIn, TOut>. "
            + "Offenders:\n  "
            + string.Join("\n  ", offenders);

        return new GuardResult(offenders, inspected, message);
    }

    private static bool IsConcreteServiceTrain(Type type)
    {
        if (type is not { IsAbstract: false, IsClass: true })
            return false;

        for (var t = type.BaseType; t is not null; t = t.BaseType)
        {
            if (t.IsGenericType && t.Name == ServiceTrainBaseName)
                return true;
        }

        return false;
    }

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
