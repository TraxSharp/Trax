using System.Reflection;
using System.Reflection.Emit;

namespace Trax.Mediator.Testing.Tests;

/// <summary>
/// Trains without a single interface of their own, emitted into an assembly of their own. They
/// cannot live in this test assembly, because <see cref="TrainGuardFixtureSelfTest"/> checks that
/// assembly and must pass.
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
        var module = assembly.DefineDynamicModule("BadTrains");

        // Derives from ServiceTrain<,> and has no train interface of its own.
        var bad = module.DefineType(
            "BadTrain",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
            typeof(ServiceTrain<int, int>)
        );
        bad.DefineDefaultConstructor(MethodAttributes.Public);
        bad.CreateType();

        // Implements IServiceTrain<,> directly, and nothing more specific.
        var direct = module.DefineType(
            "DirectOnlyTrain",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
            typeof(object),
            [typeof(IServiceTrain<int, int>)]
        );
        direct.DefineDefaultConstructor(MethodAttributes.Public);
        direct.CreateType();

        // Two train interfaces, neither extending the other: no single canonical name.
        var faces = new[] { "IFirstFace", "ISecondFace" }
            .Select(name =>
            {
                var face = module.DefineType(
                    name,
                    TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract
                );
                face.AddInterfaceImplementation(typeof(IServiceTrain<int, int>));
                return face.CreateType();
            })
            .ToArray();
        var twoFaced = module.DefineType(
            "TwoFacedTrain",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
            typeof(ServiceTrain<int, int>),
            faces
        );
        twoFaced.DefineDefaultConstructor(MethodAttributes.Public);
        twoFaced.CreateType();

        return assembly;
    }
}
