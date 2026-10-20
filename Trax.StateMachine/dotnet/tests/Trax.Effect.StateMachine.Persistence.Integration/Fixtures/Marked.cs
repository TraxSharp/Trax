using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using Trax.Effect.Attributes;

namespace Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

/// <summary>
/// A fake machine marked <see cref="TraxMachineAttribute"/>, for a test that serves it: a sealed subclass carrying the
/// attribute, built into an assembly of its own. The attribute on a type in this assembly would serve it in every
/// host that scans it, so a test that serves a fake registers this subclass on its own host instead.
/// </summary>
internal static class Marked
{
    private static readonly Lock Gate = new();
    private static readonly ConcurrentDictionary<Type, Type> Built = new();
    private static readonly ModuleBuilder Module = AssemblyBuilder
        .DefineDynamicAssembly(new AssemblyName("MarkedFakes"), AssemblyBuilderAccess.Run)
        .DefineDynamicModule("MarkedFakes");

    /// <summary>A new instance of <typeparamref name="TMachine"/>'s marked subclass.</summary>
    public static TMachine Machine<TMachine>()
        where TMachine : IMachine, new() =>
        (TMachine)Activator.CreateInstance(TypeOf(typeof(TMachine)))!;

    /// <summary>The marked subclass of <paramref name="machine"/>, which has the same simple name.</summary>
    public static Type TypeOf(Type machine) =>
        Built.GetOrAdd(
            machine,
            parent =>
            {
                lock (Gate)
                {
                    var type = Module.DefineType(
                        $"Marked.{parent.Name}",
                        TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed,
                        parent
                    );
                    type.DefineDefaultConstructor(MethodAttributes.Public);
                    type.SetCustomAttribute(
                        new CustomAttributeBuilder(
                            typeof(TraxMachineAttribute).GetConstructor(Type.EmptyTypes)!,
                            []
                        )
                    );
                    return type.CreateType();
                }
            }
        );
}
