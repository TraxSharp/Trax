using System.Reflection;
using System.Reflection.Emit;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Attributes;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// <see cref="TraxMachineAttribute"/> is the only thing that serves a machine, as <c>[TraxQuery]</c> and
/// <c>[TraxMutation]</c> are for a train: it is read from every machine the host registers, by the
/// <c>AddStateMachines</c> scan or by the host's own <see cref="IMachine"/> registration, and a marked machine is
/// under the startup checks of a machine a client can reach; an unmarked machine is not served; and the attribute
/// anywhere but on a concrete machine refuses the host. Each machine here is built into an assembly of the test's own,
/// because the attribute on a type in this one would serve it in every host that scans it.
/// </summary>
[Property("adr", Adr)]
public class MarkedMachineTests
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    [Test]
    public async Task A_marked_machine_is_served()
    {
        var assembly = Build(typeof(MarkableRuledMachine), marked: true);

        Served(assembly).Should().ContainSingle().Which.Name.Should().Be("Machine");
        (await StartingHost.StartScanning(_ => { }, assembly))
            .Should()
            .BeNull($"a marked machine with every rule declared starts. See {Adr}");
    }

    [Test]
    public void An_unmarked_machine_is_not_served()
    {
        var assembly = Build(typeof(MarkableRuledMachine), marked: false);

        Served(assembly)
            .Should()
            .BeEmpty($"a machine without the attribute has no client surface. See {Adr}");
    }

    [Test]
    public async Task A_marked_machine_the_host_registers_itself_is_served_as_a_scanned_one_is()
    {
        var type = Marked.TypeOf(typeof(MarkableRuledMachine));
        var instance = Marked.Machine<MarkablePartlyRuledMachine>();

        // By type, as an instance, and through a factory: every way a host registers an IMachine.
        var byType = ServedRegistering(s => s.AddSingleton(typeof(IMachine), type));
        var asInstance = ServedRegistering(s => s.AddSingleton<IMachine>(instance));
        var byFactory = ServedRegistering(s =>
            s.AddSingleton<IMachine>(_ => Marked.Machine<MarkableRuledMachine>())
        );

        byType
            .Should()
            .Equal(
                [type],
                $"the attribute is read from every registered machine, as train discovery reads every registered train. See {Adr}"
            );
        asInstance.Should().Equal([instance.GetType()]);
        byFactory.Should().Equal([type]);
        (await StartingHost.Start(_ => { }, Marked.Machine<MarkableRuledMachine>()))
            .Should()
            .BeNull($"a marked machine registered by hand starts like a scanned one. See {Adr}");
    }

    [Test]
    public async Task A_marked_machine_the_host_registers_itself_is_checked_as_a_scanned_one_is()
    {
        var refusal = await StartingHost.Start(
            _ => { },
            Marked.Machine<MarkablePartlyRuledMachine>()
        );

        refusal
            .Should()
            .BeOfType<InvalidOperationException>(
                $"however a marked machine is registered, it is served under the same checks. See {Adr}"
            )
            .Which.Message.Should()
            .Contain(MarkablePartlyRuledMachine.Id)
            .And.Contain("marked [TraxMachine]")
            .And.Contain(nameof(TurnstileTrigger.Push));
    }

    [Test]
    public async Task A_marked_machine_missing_a_rule_refuses_startup_and_says_it_is_marked()
    {
        var assembly = Build(typeof(MarkablePartlyRuledMachine), marked: true);

        var refusal = await StartingHost.StartScanning(_ => { }, assembly);

        refusal
            .Should()
            .BeOfType<InvalidOperationException>(
                $"the attribute serves a machine under the checks of a machine a client can reach. See {Adr}"
            )
            .Which.Message.Should()
            .Contain(MarkablePartlyRuledMachine.Id)
            .And.Contain("marked [TraxMachine]")
            .And.Contain(nameof(TurnstileTrigger.Push));
    }

    [Test]
    public void The_attribute_on_an_abstract_machine_refuses_the_host_rather_than_serving_its_subclass()
    {
        var module = NewModule();
        var marked = Define(
            module,
            "MarkedBase",
            typeof(MarkableRuledMachine),
            marked: true,
            isAbstract: true
        );
        Define(module, "UnmarkedSubclass", marked, marked: false);

        var act = () => Served(module.Assembly);

        act.Should()
            .Throw<InvalidOperationException>(
                $"the attribute is not inherited, so on a base type it would serve nothing. See {Adr}"
            )
            .Which.Message.Should()
            .Contain("MarkedBase")
            .And.Contain("[TraxMachine]");
    }

    [Test]
    public void The_attribute_on_a_class_that_is_no_machine_refuses_the_host()
    {
        var module = NewModule();
        Define(module, "NotAMachine", typeof(object), marked: true);

        var act = () => Served(module.Assembly);

        act.Should()
            .Throw<InvalidOperationException>(
                $"the attribute on a class that is no machine would serve nothing. See {Adr}"
            )
            .Which.Message.Should()
            .Contain("NotAMachine")
            .And.Contain("[TraxMachine]");
    }

    [Test]
    public void The_attribute_on_an_open_generic_machine_refuses_the_host()
    {
        var module = NewModule();
        Define(module, "OpenMachine", typeof(MarkableRuledMachine), marked: true, generic: true);

        var act = () => Served(module.Assembly);

        act.Should()
            .Throw<InvalidOperationException>(
                $"an open generic machine cannot be built, so the attribute on it would serve nothing. See {Adr}"
            )
            .Which.Message.Should()
            .Contain("OpenMachine")
            .And.Contain("[TraxMachine]");
    }

    [Test]
    public async Task An_unmarked_subclass_of_a_marked_machine_is_not_served_and_refuses_the_host()
    {
        var module = NewModule();
        var marked = Define(module, "MarkedMachine", typeof(MarkableRuledMachine), marked: true);
        Define(module, "UnmarkedSubclass", marked, marked: false);

        Served(module.Assembly)
            .Should()
            .Equal(
                [marked],
                $"the attribute is not inherited: only the marked type itself is served. See {Adr}"
            );
        var refusal = await StartingHost.StartScanning(_ => { }, module.Assembly);

        refusal
            .Should()
            .BeOfType<InvalidOperationException>(
                $"an unmarked subclass of a marked machine is not served, so its author is told. See {Adr}"
            )
            .Which.Message.Should()
            .Contain("UnmarkedSubclass")
            .And.Contain("derives from MarkedMachine")
            .And.Contain("Mark it [TraxMachine].")
            .And.NotContain("Expose");
    }

    [Test]
    public async Task A_marked_system_owned_machine_refuses_the_host_and_says_to_remove_the_attribute()
    {
        var scanned = await StartingHost.StartScanning(
            _ => { },
            Build(typeof(MarkableSystemOwnedMachine), marked: true)
        );
        var registered = await StartingHost.Start(
            _ => { },
            Marked.Machine<RuledSystemTurnstileMachine>()
        );

        foreach (var refusal in new[] { scanned, registered })
            refusal
                .Should()
                .BeOfType<InvalidOperationException>(
                    $"a system-owned machine has no client surface, however it is registered. See {Adr}"
                )
                .Which.Message.Should()
                .Contain("system-owned")
                .And.Contain("Remove [TraxMachine] from it.")
                .And.NotContain("Expose");
    }

    [Test]
    public async Task A_base_machine_registered_beside_its_marked_subclass_refuses_the_host_naming_both()
    {
        var module = NewModule();
        var served = Define(module, "ServedSubclass", typeof(MarkableRuledMachine), marked: true);

        var refusal = await StartingHost.Start(
            _ => { },
            new MarkableRuledMachine(),
            (IMachine)Activator.CreateInstance(served)!
        );

        refusal
            .Should()
            .BeOfType<InvalidOperationException>(
                $"a subclass has its base's id, and every machine is looked up by id. See {Adr}"
            )
            .Which.Message.Should()
            .Contain($"'{MarkableRuledMachine.Id}'")
            .And.Contain(typeof(MarkableRuledMachine).FullName!)
            .And.Contain(served.FullName!)
            .And.Contain($"Register only {served.FullName}");
    }

    [Test]
    public async Task Two_unrelated_machine_types_with_one_id_refuse_the_host_naming_both()
    {
        var module = NewModule();
        var first = Define(module, "FirstSibling", typeof(MarkableRuledMachine), marked: false);
        var second = Define(module, "SecondSibling", typeof(MarkableRuledMachine), marked: false);

        var refusal = await StartingHost.Start(
            _ => { },
            (IMachine)Activator.CreateInstance(first)!,
            (IMachine)Activator.CreateInstance(second)!
        );

        refusal
            .Should()
            .BeOfType<InvalidOperationException>(
                $"every machine is looked up by its id, so two types under one id break every machine request. See {Adr}"
            )
            .Which.Message.Should()
            .Contain(first.FullName!)
            .And.Contain(second.FullName!)
            .And.Contain("another id");
    }

    [Test]
    public async Task The_same_machine_type_registered_twice_starts_and_is_served_once()
    {
        var module = NewModule();
        var served = Define(module, "ServedTwice", typeof(MarkableRuledMachine), marked: true);

        var refusal = await StartingHost.Start(
            _ => { },
            (IMachine)Activator.CreateInstance(served)!,
            (IMachine)Activator.CreateInstance(served)!
        );

        refusal.Should().BeNull($"one type registered twice is one machine. See {Adr}");
        ServedRegistering(s =>
            {
                s.AddSingleton(typeof(IMachine), served);
                s.AddSingleton(typeof(IMachine), served);
            })
            .Should()
            .Equal([served], $"a machine registered twice is served once. See {Adr}");
    }

    // The machines a host that scans only the assembly serves.
    private static IReadOnlyList<Type> Served(Assembly assembly)
    {
        var services = new ServiceCollection();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory()).AddStateMachines(assembly)
        );
        return ServedBy(services);
    }

    // The machines a host serves that scans no assembly and registers its machines with register.
    private static IReadOnlyList<Type> ServedRegistering(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory()).AddStateMachines()
        );
        register(services);
        return ServedBy(services);
    }

    private static IReadOnlyList<Type> ServedBy(IServiceCollection services)
    {
        using var provider = services.BuildServiceProvider();
        return StateMachineOptions
            .Served(provider.GetServices<IMachine>())
            .Select(m => m.GetType())
            .ToList();
    }

    // An assembly of its own holding one machine deriving from the open fake, marked or not.
    private static Assembly Build(Type machine, bool marked)
    {
        var module = NewModule();
        Define(module, "Machine", machine, marked, isAbstract: false);
        return module.Assembly;
    }

    private static ModuleBuilder NewModule() =>
        AssemblyBuilder
            .DefineDynamicAssembly(
                new AssemblyName($"MarkedMachines_{Guid.NewGuid():N}"),
                AssemblyBuilderAccess.Run
            )
            .DefineDynamicModule("MarkedMachines");

    private static Type Define(
        ModuleBuilder module,
        string name,
        Type parent,
        bool marked,
        bool isAbstract = false,
        bool generic = false
    )
    {
        var type = module.DefineType(
            name,
            TypeAttributes.Public
                | TypeAttributes.Class
                | (isAbstract ? TypeAttributes.Abstract : 0),
            parent
        );
        if (generic)
            type.DefineGenericParameters("T");
        type.DefineDefaultConstructor(MethodAttributes.Public);
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
