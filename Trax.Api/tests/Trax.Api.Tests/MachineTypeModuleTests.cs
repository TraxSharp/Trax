using AwesomeAssertions;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.PersistedOperations.Services;
using Trax.Api.GraphQL.Validation;
using Trax.Api.Tests.Fakes.Machines;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Api.Tests;

/// <summary>
/// The types the <c>machines</c> root generates for each exposed machine: one input type per record however many
/// machines and trains share it, one field per machine whose name is a GraphQL name no other machine claims, no event
/// named like the operations every machine has, generated names that never take a CLR type's, every field's posture
/// declared, the census of each event and read, and no field for a trigger only the server fires.
/// <para>Enforces <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
[TestFixture]
public class MachineTypeModuleTests
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private static IReadOnlyList<Type> Serve<T1>()
        where T1 : IMachine => [typeof(T1)];

    private static IReadOnlyList<Type> Serve<T1, T2>()
        where T1 : IMachine
        where T2 : IMachine => [typeof(T1), typeof(T2)];

    #region Input types

    [Test]
    public async Task CreateTypes_TwoMachinesShareAnInputRecord_RegistersItOnce()
    {
        var (host, executor) = await MachineSchemaHost.StartWithSchemaAsync(
            Serve<ShopMachine, PartsMachine>()
        );
        using var _ = host;

        var shop = MachineSchemaHost.MachineType(executor, MachineSchemaHost.RootMutation, "shop");
        var parts = MachineSchemaHost.MachineType(
            executor,
            MachineSchemaHost.RootMutation,
            "parts"
        );
        shop.Should().NotBeNull("a marked machine has its own field under machines, per " + Adr);
        parts.Should().NotBeNull("a marked machine has its own field under machines, per " + Adr);

        var shopInput = shop!.Fields["ship"].Arguments["input"].Type.NamedType();
        var partsInput = parts!.Fields["ship"].Arguments["input"].Type.NamedType();
        shopInput
            .Should()
            .BeSameAs(
                partsInput,
                "two machines whose events take one record share one input type, per " + Adr
            );
        executor
            .Schema.Types.OfType<InputObjectType>()
            .Count(t => t.RuntimeType == typeof(ShipToInput))
            .Should()
            .Be(1, "a record is registered once however many machines take it");
    }

    [Test]
    public async Task CreateTypes_MachineInputRecordAlsoATrainInput_DoesNotCollide()
    {
        var (host, executor) = await MachineSchemaHost.StartWithSchemaAsync(
            Serve<PartsMachine>(),
            trains:
            [
                MachineSchemaHost.Train<
                    ICatalogLookupTrain,
                    CatalogLookupInput,
                    CatalogLookupOutput
                >(isQuery: true),
            ]
        );
        using var _ = host;

        var parts = MachineSchemaHost.MachineType(
            executor,
            MachineSchemaHost.RootMutation,
            "parts"
        );
        parts.Should().NotBeNull("a marked machine has its own field under machines, per " + Adr);
        var eventInput = parts!.Fields["lookup"].Arguments["input"].Type.NamedType();

        executor
            .Schema.Types.OfType<InputObjectType>()
            .Where(t => t.RuntimeType == typeof(CatalogLookupInput))
            .Should()
            .ContainSingle(
                "a record a train and a machine event both take is one input type, per " + Adr
            )
            .Which.Should()
            .BeSameAs(eventInput);
    }

    #endregion

    #region Names

    // A machine id is kebab-case and starts with a letter (the builder refuses any other), so an id that is no
    // GraphQL name never reaches the schema; the ids that still collide are the ones a hyphen before a digit
    // separates.
    [Test]
    public async Task MachineNames_ThatNormaliseToOneField_FailStartupNamingBoth()
    {
        var refusal = await MachineSchemaHost.RefusalAsync(
            Serve<Map2dHyphenMachine, Map2dMachine>()
        );

        refusal
            .Should()
            .NotBeNull(
                "two machines whose ids normalise to one field (map-2d and map2d are both map2d) refuse "
                    + "startup, per "
                    + Adr
            );
        refusal!
            .ToString()
            .Should()
            .Contain("'map-2d'")
            .And.Contain("'map2d'", "the refusal names both machines");
    }

    [TestCase(typeof(StartEventMachine), "start-event", "Start")]
    [TestCase(typeof(DiscardEventMachine), "discard-event", "Discard")]
    [TestCase(typeof(GetEventMachine), "get-event", "Get")]
    public async Task AnEventNamedStartDiscardOrGet_FailsStartup(
        Type machine,
        string id,
        string trigger
    )
    {
        var refusal = await MachineSchemaHost.RefusalAsync([machine]);

        refusal
            .Should()
            .NotBeNull(
                "an event named like an operation every machine has would collide with it, per "
                    + Adr
            );
        refusal!.ToString().Should().Contain($"'{id}'").And.Contain(trigger);
    }

    [Test]
    public async Task GeneratedTypeNames_DoNotCollideWithAClrTypeOfTheSameName()
    {
        var (host, executor) = await MachineSchemaHost.StartWithSchemaAsync(
            Serve<LedgerMachine>(),
            trains:
            [
                MachineSchemaHost.Train<
                    ILedgerSummaryTrain,
                    LedgerSummaryInput,
                    LedgerSummaryOutput
                >(isQuery: true),
            ]
        );
        using var _ = host;

        var get = MachineSchemaHost.MachineType(executor, MachineSchemaHost.RootQuery, "ledger");
        get.Should().NotBeNull("a marked machine has its own field under machines, per " + Adr);
        var snapshot = (ObjectType)get!.Fields["get"].Type.NamedType();
        var machineState = (EnumType)snapshot.Fields["state"].Type.NamedType();

        var clrState = executor
            .Schema.Types.OfType<EnumType>()
            .Single(t => t.RuntimeType == typeof(LedgerState));

        machineState
            .Should()
            .NotBeSameAs(clrState, "a generated type never takes a CLR type's place, per " + Adr);
        machineState.Name.Should().NotBe(clrState.Name);
        machineState
            .Values.Select(v => v.Name)
            .Should()
            .HaveCount(Enum.GetValues<LedgerStage>().Length, "the machine's own states");
        clrState
            .Values.Select(v => v.Name)
            .Should()
            .HaveCount(Enum.GetValues<LedgerState>().Length, "the train's enum keeps its values");
    }

    [Test]
    public async Task AHostContributedMachinesRootField_FailsStartup()
    {
        var refusal = await MachineSchemaHost.RefusalAsync(
            Serve<ShopMachine>(),
            graphql => graphql.ConfigureSchema(s => s.AddTypeExtension<HostMachinesField>())
        );

        refusal
            .Should()
            .NotBeNull(
                "a host field named machines on the root would merge with Trax's, so it refuses startup, "
                    + "per "
                    + Adr
            );
        refusal!
            .ToString()
            .Should()
            .Contain("RootMutation.machines")
            .And.Contain(typeof(HostMachinesResolvers).FullName!);
    }

    #endregion

    #region Posture and census

    [Test]
    public async Task MachinesFields_DeclareTheirPosture_UnderTheCensus()
    {
        var (host, executor) = await MachineSchemaHost.StartWithSchemaAsync(Serve<ShopMachine>());
        using var _ = host;

        foreach (var root in new[] { MachineSchemaHost.RootQuery, MachineSchemaHost.RootMutation })
        {
            var rootType = MachineSchemaHost.ObjectTypeNamed(executor, root)!;
            rootType
                .Fields.TryGetField("machines", out var machines)
                .Should()
                .BeTrue($"{root} has the machines root, per {Adr}");
            NamespaceField
                .IsNamespace(machines!)
                .Should()
                .BeTrue("machines groups fields and does no work of its own");

            var namespaceType = (ObjectType)machines!.Type.NamedType();
            var shop = namespaceType.Fields["shop"];
            NamespaceField
                .IsNamespace(shop)
                .Should()
                .BeTrue("a machine's field groups its operations");

            foreach (var field in ((ObjectType)shop.Type.NamedType()).Fields)
            {
                if (field.IsIntrospectionField)
                    continue;
                field
                    .Directives.Any(d => d.Type.Name == "authorize")
                    .Should()
                    .BeTrue(
                        $"{root}.machines.shop.{field.Name} declares its rule as a directive, per "
                            + "api/0003 and "
                            + Adr
                    );
            }
        }
    }

    [Test]
    public void Census_ListsEachEventAndReadWithItsRule_ForTheSurfaceAndForHostCode()
    {
        var entries = MachineCensus.Describe([new ShopMachine()]);

        entries
            .Where(e => e.Kind == nameof(Trax.Effect.StateMachine.TriggerKind.Client))
            .Select(e => e.Name)
            .Should()
            .BeEquivalentTo(
                ["Submit", "Ship", "Approve", "Reopen", "Restart"],
                "every client event is listed, per " + Adr
            );
        entries
            .Where(e => e.Kind == "Read")
            .Select(e => e.Name)
            .Should()
            .BeEquivalentTo(["start", "get", "changed"], "every read is listed with its rule");

        var approve = entries.Single(e => e.Name == "Approve");
        approve.Rule.Should().Contain("Approver");
        approve
            .Surfaces.Should()
            .HaveCountGreaterThan(1, "the typed surface reaches it, and so can host code");
        approve
            .Surfaces.Should()
            .Contain(
                s => s.Contains("host", StringComparison.OrdinalIgnoreCase),
                "the census says which events host code can send"
            );
        entries.Should().OnlyContain(e => e.Machine == ShopMachine.MachineId);
    }

    #endregion

    #region Server-only triggers

    [Test]
    public async Task No_mutation_exists_for_a_timer_trigger()
    {
        var (host, executor) = await MachineSchemaHost.StartWithSchemaAsync(Serve<ShopMachine>());
        using var _ = host;

        var shop = MachineSchemaHost.MachineType(executor, MachineSchemaHost.RootMutation, "shop");
        shop.Should().NotBeNull("a marked machine has its own field under machines, per " + Adr);
        shop!
            .Fields.Where(f => !f.IsIntrospectionField)
            .Select(f => f.Name)
            .Should()
            .BeEquivalentTo(
                ["start", "discard", .. ShopMachine.ClientEventFields],
                "a timer is fired only by the server, so it has no mutation, per " + Adr
            );

        var documents = MachineDocuments.Generate(executor.Schema, [new ShopMachine()]);
        documents
            .Select(d => d.Operation)
            .Should()
            .BeEquivalentTo(
                ["start", "discard", "get", "changed", .. ShopMachine.ClientEventFields],
                "no persisted document fires a timer either: the documents are the operations and the instance's push"
            );
    }

    #endregion

    private interface ICatalogLookupTrain;

    private interface ILedgerSummaryTrain;

    // Built with a descriptor rather than [ExtendObjectType], so a test that scans this assembly for type
    // extensions does not pick it up.

    /// <summary>A host's own <c>machines</c> field on the mutation root, with its posture declared.</summary>
    public sealed class HostMachinesField : ObjectTypeExtension
    {
        protected override void Configure(IObjectTypeDescriptor descriptor)
        {
            descriptor.Name(MachineSchemaHost.RootMutation);
            descriptor.Field<HostMachinesResolvers>(r => r.Machines());
        }
    }

    public sealed class HostMachinesResolvers
    {
        [Trax.Effect.Attributes.TraxAllowAnonymous]
        public string Machines() => "host";
    }
}
