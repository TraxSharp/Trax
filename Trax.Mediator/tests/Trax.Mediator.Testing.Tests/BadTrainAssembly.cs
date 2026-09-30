using System.Reflection;
using System.Reflection.Emit;

namespace Trax.Mediator.Testing.Tests;

/// <summary>
/// A train with no companion interface, emitted into an assembly of its own. It cannot live in this
/// test assembly, because <see cref="TrainGuardFixtureSelfTest"/> checks that assembly and must pass.
/// </summary>
internal static class BadTrainAssembly
{
    public static Assembly Instance { get; } = Emit();

    private static Assembly Emit()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("Trax.Mediator.Testing.Tests.BadTrains"),
            AssemblyBuilderAccess.Run
        );
        var type = assembly
            .DefineDynamicModule("BadTrains")
            .DefineType(
                "BadTrain",
                TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
                typeof(ServiceTrain<int, int>)
            );
        type.DefineDefaultConstructor(MethodAttributes.Public);
        type.CreateType();

        return assembly;
    }
}
