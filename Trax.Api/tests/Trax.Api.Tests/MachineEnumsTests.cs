using AwesomeAssertions;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Systematic.Orders;
using Trax.Api.Tests.Fakes.Machines;
using Trax.Core.Functional;

namespace Trax.Api.Tests;

/// <summary>
/// The enums a served machine holds are spelled on the wire as the server stores them. A member whose stored name
/// GraphQL cannot carry, an enum a train also reaches, and an enum the host binds a type to itself are each refused at
/// startup in Trax's words; only <c>System</c> and the namespaces under it count as the framework's.
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
}
