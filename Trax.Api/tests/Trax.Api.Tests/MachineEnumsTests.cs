using System.Reflection;
using System.Reflection.Emit;
using AwesomeAssertions;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Systematic.Orders;
using Trax.Api.Tests.Fakes.Machines;
using Trax.Core.Functional;

namespace Trax.Api.Tests;

/// <summary>
/// The enums a served machine holds are spelled on the wire as the server stores them. A member whose stored name
/// GraphQL cannot carry, an enum a train also reaches, an enum the host binds a type to itself, and an enum of Trax's
/// own are each refused at startup in Trax's words; only <c>System</c> and the namespaces under it count as the framework's.
/// <para>Enforces <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
[TestFixture]
public class MachineEnumsTests
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    [Test]
    public async Task Enum_member_named_null_is_refused_with_a_trax_message()
    {
        var refusal = await MachineSchemaHost.RefusalAsync([typeof(NullMemberMachine)]);

        refusal.Should().NotBeNull("null is a GraphQL literal and no enum value, per " + Adr);
        refusal!
            .ToString()
            .Should()
            .Contain(typeof(NullMember).FullName!)
            .And.Contain("'null'")
            .And.Contain("Rename the member");
    }

    [Test]
    public async Task Enum_member_starting_with_two_underscores_is_refused_with_a_trax_message()
    {
        var refusal = await MachineSchemaHost.RefusalAsync([typeof(DunderMemberMachine)]);

        refusal.Should().NotBeNull("GraphQL keeps names starting with __ for itself, per " + Adr);
        refusal!
            .ToString()
            .Should()
            .Contain(typeof(DunderMember).FullName!)
            .And.Contain("'__Hidden'")
            .And.Contain("Rename the member");
    }

    [Test]
    public async Task Enum_under_a_System_prefixed_consumer_namespace_is_spelled_as_stored()
    {
        var (host, executor) = await MachineSchemaHost.StartWithSchemaAsync([
            typeof(GradedMachine),
        ]);
        using var _ = host;

        var grade = executor
            .Schema.Types.OfType<EnumType>()
            .Single(t => t.RuntimeType == typeof(Grade));

        grade
            .Values.Select(v => v.Name)
            .Should()
            .Equal(
                ["Low", "VeryHigh"],
                "Systematic.Orders is the consumer's namespace, not the framework's, so the record it holds is "
                    + "walked and its enum spelled as the server stores it, per "
                    + Adr
            );
    }

    [Test]
    public async Task An_enum_shared_by_a_machine_and_a_train_is_reported()
    {
        var refusal = await MachineSchemaHost.RefusalAsync(
            [typeof(SharedPriorityMachine)],
            trains:
            [
                MachineSchemaHost.Train<ISharedPriorityTrain, SharedPriorityInput, Unit>(
                    isQuery: true
                ),
            ]
        );

        refusal
            .Should()
            .NotBeNull(
                "the train's wire would change from VERY_HIGH to VeryHigh without a word, per "
                    + Adr
            );
        refusal!
            .ToString()
            .Should()
            .Contain(typeof(SharedPriority).FullName!)
            .And.Contain($"'{SharedPriorityMachine.MachineId}'")
            .And.Contain(typeof(ISharedPriorityTrain).FullName!)
            .And.Contain("ShareEnumWithMachines<SharedPriority>()");
    }

    [Test]
    public async Task An_enum_shared_by_a_machine_and_a_train_starts_when_the_host_accepts_it()
    {
        var (host, executor) = await MachineSchemaHost.StartWithSchemaAsync(
            [typeof(SharedPriorityMachine)],
            graphql => graphql.ShareEnumWithMachines<SharedPriority>(),
            trains:
            [
                MachineSchemaHost.Train<ISharedPriorityTrain, SharedPriorityInput, Unit>(
                    isQuery: true
                ),
            ]
        );
        using var _ = host;

        executor
            .Schema.Types.OfType<EnumType>()
            .Single(t => t.RuntimeType == typeof(SharedPriority))
            .Values.Select(v => v.Name)
            .Should()
            .Equal(["Low", "VeryHigh"], "the host accepted the machine's spelling, per " + Adr);
    }

    [Test]
    public async Task An_enum_the_host_binds_its_own_type_to_is_refused_with_a_trax_message()
    {
        var refusal = await MachineSchemaHost.RefusalAsync(
            [typeof(HostBoundMachine)],
            graphql => graphql.ConfigureSchema(s => s.AddType(new EnumType<HostBound>()))
        );

        refusal
            .Should()
            .NotBeNull("GraphQL takes one type per CLR enum, and a machine's has its, per " + Adr);
        refusal!
            .ToString()
            .Should()
            .Contain(typeof(HostBound).FullName!)
            .And.Contain("Remove the host's type");
    }

    [Test]
    public async Task Sharing_a_trax_enum_with_machines_is_refused()
    {
        var unshared = await MachineSchemaHost.RefusalAsync([typeof(TrainStateHoldingMachine)]);
        var shared = await MachineSchemaHost.RefusalAsync(
            [typeof(TrainStateHoldingMachine)],
            graphql => graphql.ShareEnumWithMachines<Trax.Effect.Enums.TrainState>()
        );

        foreach (var refusal in new[] { unshared, shared })
        {
            refusal
                .Should()
                .NotBeNull(
                    "onTrainStateChanged's trainState and the operations filters would change from COMPLETED to "
                        + "Completed, and the dashboard sends COMPLETED, so Trax never shares its enums, per "
                        + Adr
                );
            refusal!
                .ToString()
                .Should()
                .Contain(typeof(Trax.Effect.Enums.TrainState).FullName!)
                .And.Contain("Trax's own")
                .And.Contain("Give the machine its own enum")
                .And.NotContain("to accept the new spelling");
        }
        unshared!.ToString().Should().Contain($"'{TrainStateHoldingMachine.MachineId}'");
        shared!.ToString().Should().Contain("ShareEnumWithMachines<TrainState>()");
    }

    [Test]
    public void A_sample_or_test_enum_is_not_trax_s_own()
    {
        Trax.Api.GraphQL.TypeModules.MachineEnums.IsTrax(typeof(Trax.Effect.Enums.TrainState))
            .Should()
            .BeTrue("Trax.Effect is a Trax assembly, per " + Adr);
        Trax.Api.GraphQL.TypeModules.MachineEnums.IsTrax(typeof(SharedPriority))
            .Should()
            .BeFalse("a test project's enum is consumer code, per " + Adr);
    }

    [Test]
    public void An_enum_in_an_assembly_named_like_trax_is_refused_in_words_that_say_why()
    {
        var name = new AssemblyName("Trax.Billing");
        var module = AssemblyBuilder
            .DefineDynamicAssembly(name, AssemblyBuilderAccess.Run)
            .DefineDynamicModule(name.Name!);
        var plan = module.DefineEnum("Billing.Plan", TypeAttributes.Public, typeof(int));
        plan.DefineLiteral("Free", 0);
        var billing = plan.CreateType();
        var builder = new Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder.TraxGraphQLBuilder(
            new ServiceCollection()
        );

        var share = () =>
            builder
                .GetType()
                .GetMethod("ShareEnumWithMachines")!
                .MakeGenericMethod(billing)
                .Invoke(builder, null);

        share
            .Should()
            .Throw<TargetInvocationException>()
            .WithInnerException<InvalidOperationException>()
            .Which.Message.Should()
            .Contain("Billing.Plan")
            .And.Contain(
                "declared in Trax.Billing, and an assembly named Trax or Trax.* counts as Trax's own",
                "Trax tells its own enums by the assembly's name, so the refusal says so, per "
                    + Adr
            );
    }

    [Test]
    public async Task An_enum_a_host_type_extension_returns_is_refused()
    {
        var refusal = await MachineSchemaHost.RefusalAsync(
            [typeof(ExtendedPriorityMachine)],
            graphql => graphql.ConfigureSchema(s => s.AddTypeExtension<HostPriorityField>())
        );

        refusal.Should().NotBeNull("the host's field would change its spelling too, per " + Adr);
        refusal!
            .ToString()
            .Should()
            .Contain(typeof(ExtendedPriority).FullName!)
            .And.Contain("'RootQuery.hostPriority'");
    }

    [Test]
    public async Task An_enum_a_host_type_extension_returns_starts_when_the_host_accepts_it()
    {
        var (host, executor) = await MachineSchemaHost.StartWithSchemaAsync(
            [typeof(ExtendedPriorityMachine)],
            graphql =>
                graphql
                    .ShareEnumWithMachines<ExtendedPriority>()
                    .ConfigureSchema(s => s.AddTypeExtension<HostPriorityField>())
        );
        using var _ = host;

        executor
            .Schema.Types.OfType<EnumType>()
            .Single(t => t.RuntimeType == typeof(ExtendedPriority))
            .Values.Select(v => v.Name)
            .Should()
            .Equal(["Low", "VeryHigh"], "the host accepted the machine's spelling, per " + Adr);
    }

    [Test]
    public async Task An_enum_inside_a_list_subclass_is_reached()
    {
        var (host, executor) = await MachineSchemaHost.StartWithSchemaAsync([
            typeof(ListedPriorityMachine),
        ]);
        using var _ = host;

        executor
            .Schema.Types.OfType<EnumType>()
            .Single(t => t.RuntimeType == typeof(ListedPriority))
            .Values.Select(v => v.Name)
            .Should()
            .Equal(
                ["Low", "VeryHigh"],
                "a class deriving from List<T> is a list of T, so its enum is spelled as the server stores it, "
                    + "per "
                    + Adr
            );
    }

    [Test]
    public void An_enum_inside_a_framework_generic_is_reached()
    {
        Trax.Api.GraphQL.TypeModules.MachineEnums.Reached(typeof(FrameworkGenerics))
            .Should()
            .BeEquivalentTo(
                [typeof(PairedPriority), typeof(TupledPriority), typeof(ValueTupledPriority)],
                "KeyValuePair, Tuple and ValueTuple hold what their arguments hold, per " + Adr
            );
    }
}
