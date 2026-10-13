using System.Reflection;
using System.Reflection.Emit;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.Tests.Fakes.Machines;
using Trax.Effect.Attributes;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Api.Tests;

/// <summary>
/// A machine marked <see cref="TraxMachineAttribute"/> has its field under <c>machines</c> in the schema however it is
/// registered, as a train marked <c>[TraxQuery]</c> or <c>[TraxMutation]</c> is served however it is registered: found
/// by the <c>AddStateMachines</c> scan, or registered by the host as <see cref="IMachine"/> by its type, as an instance
/// or through a factory. The attribute is not inherited: a host registering an unmarked subclass of a marked machine
/// refuses to start rather than leave the subclass silently unserved. The scanned machines are built into an assembly
/// of the test's own, because no host scans this one.
/// <para>Enforces <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
[TestFixture]
public class MarkedMachineSchemaTests
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    [Test]
    public async Task A_marked_machine_has_its_field_in_the_schema()
    {
        var module = NewModule();
        Define(
            module,
            "MarkedStepMachine",
            typeof(NamedStepMachine),
            marked: true,
            id: "marked-step"
        );

        using var host = await MachineSchemaHost.StartAsync([], scan: module.Assembly);
        var executor = await MachineSchemaHost.ExecutorAsync(host);

        MachineSchemaHost
            .MachineType(executor, MachineSchemaHost.RootMutation, "markedStep")
            .Should()
            .NotBeNull($"a marked machine is served as an exposed one, per {Adr}");
        MachineSchemaHost
            .MachineType(executor, MachineSchemaHost.RootQuery, "markedStep")
            .Should()
            .NotBeNull($"a marked machine is served as an exposed one, per {Adr}");
    }

    [Test]
    public async Task A_marked_machine_the_host_registers_itself_has_its_field_in_the_schema()
    {
        using var host = await MachineSchemaHost.StartAsync(
            [],
            services: s => s.AddSingleton<IMachine>(new PartsMachine())
        );
        var executor = await MachineSchemaHost.ExecutorAsync(host);

        foreach (var root in new[] { MachineSchemaHost.RootMutation, MachineSchemaHost.RootQuery })
            MachineSchemaHost
                .MachineType(executor, root, "parts")
                .Should()
                .NotBeNull(
                    $"the attribute is read from every registered machine, not only the scanned ones, per {Adr}"
                );
    }

    [Test]
    public async Task A_marked_machine_registered_through_a_factory_is_served_beside_one_the_schema_saw()
    {
        using var host = await MachineSchemaHost.StartAsync(
            [typeof(ShopMachine)],
            services: s => s.AddSingleton<IMachine>(_ => new PartsMachine())
        );
        var executor = await MachineSchemaHost.ExecutorAsync(host);

        MachineSchemaHost
            .MachineType(executor, MachineSchemaHost.RootMutation, "parts")
            .Should()
            .NotBeNull($"a marked machine is served however it is registered, per {Adr}");
    }

    [Test]
    public async Task A_marked_machine_only_a_factory_registers_refuses_the_host_rather_than_go_unserved()
    {
        var refusal = await MachineSchemaHost.RefusalAsync(
            [],
            services: s => s.AddSingleton<IMachine>(_ => new PartsMachine())
        );

        refusal
            .Should()
            .NotBeNull(
                $"AddTraxGraphQL cannot read a factory, so with no other marked machine the schema has no machines root, and the host says so rather than leave the machine unserved, per {Adr}"
            );
        refusal!
            .ToString()
            .Should()
            .Contain($"'{PartsMachine.MachineId}'")
            .And.Contain("[TraxMachine]")
            .And.Contain("AddTraxGraphQL()");
    }

    [Test]
    public async Task An_unmarked_subclass_of_a_marked_machine_refuses_the_host_and_is_not_served()
    {
        var module = NewModule();
        var marked = Define(
            module,
            "MarkedStepMachine",
            typeof(NamedStepMachine),
            marked: true,
            id: "marked-step"
        );
        Define(module, "UnmarkedStepSubclass", marked, marked: false);

        var refusal = await MachineSchemaHost.RefusalAsync([], scan: module.Assembly);

        refusal
            .Should()
            .NotBeNull(
                $"the attribute is not inherited, so an unmarked subclass is not served and says so, per {Adr}"
            );
        refusal!
            .ToString()
            .Should()
            .Contain("UnmarkedStepSubclass")
            .And.Contain("derives from MarkedStepMachine");
    }

    [Test]
    public async Task A_base_machine_registered_beside_its_marked_subclass_refuses_the_host_naming_both()
    {
        var module = NewModule();
        var library = Define(
            module,
            "LibraryStepMachine",
            typeof(NamedStepMachine),
            marked: false,
            id: "library-step"
        );
        var served = Define(module, "ServedLibraryStepMachine", library, marked: true);

        var refusal = await MachineSchemaHost.RefusalAsync([library, served]);

        refusal
            .Should()
            .NotBeNull(
                $"a subclass has its base's id, and every machine is looked up by id, so the two would break every machine request, per {Adr}"
            );
        refusal!
            .ToString()
            .Should()
            .Contain("'library-step'")
            .And.Contain(library.FullName!)
            .And.Contain(served.FullName!)
            .And.Contain($"Register only {served.FullName}");
    }

    [Test]
    public async Task The_same_marked_machine_registered_twice_starts_and_is_served_once()
    {
        using var host = await MachineSchemaHost.StartAsync([
            typeof(PartsMachine),
            typeof(PartsMachine),
            typeof(ShopMachine),
        ]);
        var executor = await MachineSchemaHost.ExecutorAsync(host);

        var machines = MachineSchemaHost.FieldType(
            MachineSchemaHost.ObjectTypeNamed(executor, MachineSchemaHost.RootQuery),
            "machines"
        );
        machines.Should().NotBeNull($"a machine registered twice is one machine, per {Adr}");
        machines!
            .Fields.Where(f => !f.IsIntrospectionField)
            .Select(f => f.Name)
            .Should()
            .BeEquivalentTo(["parts", "shop"]);
    }

    private static ModuleBuilder NewModule() =>
        AssemblyBuilder
            .DefineDynamicAssembly(
                new AssemblyName($"MarkedSchemaMachines_{Guid.NewGuid():N}"),
                AssemblyBuilderAccess.Run
            )
            .DefineDynamicModule("MarkedSchemaMachines");

    // A public machine deriving from parent, with a parameterless constructor that passes id to the parent's
    // (string id) constructor when one is given, or calls its parameterless one.
    private static Type Define(
        ModuleBuilder module,
        string name,
        Type parent,
        bool marked,
        string? id = null
    )
    {
        var type = module.DefineType(name, TypeAttributes.Public | TypeAttributes.Class, parent);
        if (id is null)
            type.DefineDefaultConstructor(MethodAttributes.Public);
        else
        {
            var baseConstructor = parent.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                [typeof(string)]
            )!;
            var il = type.DefineConstructor(
                    MethodAttributes.Public,
                    CallingConventions.Standard,
                    Type.EmptyTypes
                )
                .GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, id);
            il.Emit(OpCodes.Call, baseConstructor);
            il.Emit(OpCodes.Ret);
        }
        if (marked)
            type.SetCustomAttribute(
                new CustomAttributeBuilder(
                    typeof(TraxMachineAttribute).GetConstructor(Type.EmptyTypes)!,
                    []
                )
            );
        return type.CreateType();
    }
}
