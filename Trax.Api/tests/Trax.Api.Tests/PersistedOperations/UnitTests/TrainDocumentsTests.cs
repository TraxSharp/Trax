using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Mutations;
using Trax.Api.GraphQL.PersistedOperations.Services;
using Trax.Api.GraphQL.TypeModules;
using Trax.Effect.Attributes;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Api.Tests.PersistedOperations.UnitTests;

/// <summary>
/// Every exposed train gets a persisted document under <c>trax_train_</c>: an id that reads back to one slot whatever
/// its names hold, a hash of the train's shape that moves when an input or output field does and not when a doc
/// comment or another train does, a document that selects the whole answer short of a field leading back to a type
/// already on the path, and a refusal of a field no document can select.
/// <para>Enforces <c>docs/adr/0040-every-exposed-train-is-a-persisted-document-the-host-registers.md</c>.</para>
/// </summary>
[TestFixture]
[Property("adr", "docs/adr/0040-every-exposed-train-is-a-persisted-document-the-host-registers.md")]
public class TrainDocumentsTests
{
    private const string Adr =
        "docs/adr/0040-every-exposed-train-is-a-persisted-document-the-host-registers.md";

    private static readonly string Hash =
        new string('a', MachineDocuments.IrHashLength)
        + new string('b', MachineDocuments.TextHashLength);

    #region Ids

    [Test]
    public void An_underscore_in_a_namespace_or_name_reads_back_and_never_collides()
    {
        var first = TrainDocuments.SlotOf("mutation", "a_b", "c");
        var second = TrainDocuments.SlotOf("mutation", "a", "b_c");

        first.Should().NotBe(second);
        TrainDocuments.SlotOf(first + "_" + Hash).Should().Be(first);
        TrainDocuments.SlotOf(second + "_" + Hash).Should().Be(second);
    }

    [Test]
    public void An_id_without_a_namespace_reads_back()
    {
        var slot = TrainDocuments.SlotOf("query", null, "findOrder");

        slot.Should().Be("trax_train_query__findOrder");
        TrainDocuments.SlotOf(slot + "_" + Hash).Should().Be(slot);
    }

    [TestCase("trax_train_mutation__startRun")]
    [TestCase("trax_train_subscription__startRun_HASH")]
    [TestCase("trax_train_mutation___HASH")]
    [TestCase("trax_machine_order_get_HASH")]
    [TestCase("recovery_StartRun_v1")]
    public void An_id_that_is_not_a_train_document_names_no_slot(string id) =>
        TrainDocuments.SlotOf(id.Replace("HASH", Hash)).Should().BeNull();

    [Test]
    public void Machine_and_train_ids_never_share_a_slot()
    {
        var train = TrainDocuments.SlotOf("mutation", null, "order") + "_" + Hash;

        GeneratedDocuments.SlotOf(train, ["order"]).Should().BeNull();
        GeneratedDocuments.IsReserved(train).Should().BeTrue();
        GeneratedDocuments.PrefixOf(train).Should().Be(TrainDocuments.ReservedIdPrefix);
    }

    #endregion

    #region Documents

    [Test]
    public async Task A_mutation_takes_its_arguments_as_variables_and_selects_the_handle_and_output()
    {
        var document = await OneAsync(Mutation<StartInput>("StartRun", typeof(RunOutput)));

        document.Id.Should().StartWith("trax_train_mutation__startRun_");
        document
            .Text.Should()
            .StartWith(
                "mutation StartRun($input: StartInput!, $mode: ExecutionMode, $priority: Int"
            )
            .And.Contain("dispatch { startRun(input: $input, mode: $mode, priority: $priority)")
            .And.Contain("externalId")
            .And.Contain("output { runId steps { name done } }");
    }

    [Test]
    public async Task A_train_without_an_output_selects_only_its_run_handle()
    {
        var document = await OneAsync(
            Mutation<StartInput>("Fire", typeof(Trax.Core.Functional.Unit), GraphQLOperation.Run)
        );

        document.Text.Should().NotContain("output").And.Contain("externalId");
    }

    [Test]
    public async Task A_namespaced_query_is_reached_through_its_namespace()
    {
        var document = await OneAsync(
            Query<StartInput>("FindRun", typeof(RunOutput), graphqlNamespace: "runs")
        );

        document.Id.Should().StartWith("trax_train_query_runs_findRun_");
        document.Text.Should().Contain("discover { runs { findRun(input: $input)");
    }

    [Test]
    public async Task An_answer_with_a_back_reference_generates_a_document_without_it()
    {
        var document = await OneAsync(Query<StartInput>("FindParent", typeof(Parent)));

        document
            .Text.Should()
            .Contain("findParent(input: $input) { name children { name } }", "per " + Adr);
        document.LeftOut.Should().Equal("Parent.children > Child.parent");
    }

    [Test]
    public async Task A_paged_back_reference_is_left_out_whole()
    {
        var document = await OneAsync(Query<StartInput>("FindChild", typeof(PagedChild)));

        document
            .Text.Should()
            .Contain("findChild(input: $input) { name parent { name } }")
            .And.NotContain("edges")
            .And.NotContain("nodes")
            .And.NotContain("pageInfo");
        document.LeftOut.Should().Equal("PagedChild.parent > PagedParent.children");
    }

    [Test]
    public async Task A_self_recursive_answer_selects_its_own_fields_only()
    {
        var document = await OneAsync(Query<StartInput>("Tree", typeof(Node)));

        document.Text.Should().Contain("tree(input: $input) { name }");
        document.LeftOut.Should().Equal("Node.children");
    }

    [Test]
    public async Task A_type_reached_from_two_parents_is_selected_under_both()
    {
        var document = await OneAsync(Query<StartInput>("FindOrder", typeof(Order)));

        document
            .Text.Should()
            .Contain(
                "findOrder(input: $input) { billing { city } shipping { city } }",
                "two parents are not a cycle"
            );
        document.LeftOut.Should().BeEmpty();
    }

    [Test]
    public async Task A_field_with_a_required_argument_refuses_naming_the_train_and_the_field()
    {
        var act = () => OneAsync(Query<StartInput>("Lookup", typeof(Lookup)));

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*Lookup.find*required argument 'key'*");
    }

    #endregion

    #region Shape

    [Test]
    public async Task Adding_a_field_to_the_input_moves_the_id()
    {
        var before = await OneAsync(Mutation<ShapeInputV1>("Shape", typeof(RunOutput)));
        var after = await OneAsync(Mutation<ShapeInputV2>("Shape", typeof(RunOutput)));

        after.Slot.Should().Be(before.Slot);
        after.Id.Should().NotBe(before.Id, "a new input field is a new shape, per " + Adr);
    }

    [Test]
    public async Task A_doc_comment_or_another_train_leaves_the_id_alone()
    {
        var alone = await OneAsync(Mutation<ShapeInputV1>("Shape", typeof(RunOutput)));
        var described = await OneAsync(Mutation<ShapeInputDescribed>("Shape", typeof(RunOutput)));
        var withSibling = (
            await GenerateAsync(
                Mutation<ShapeInputV1>("Shape", typeof(RunOutput)),
                Mutation<StartInput>("Other", typeof(RunOutput))
            )
        ).Single(d => d.Field == "shape");

        described.Id.Should().Be(alone.Id, "a description is not part of the shape");
        withSibling.Id.Should().Be(alone.Id, "another train is not part of this one's shape");
    }

    [Test]
    public async Task A_user_type_with_a_wrapper_suffix_still_moves_the_shape_hash()
    {
        var before = await OneAsync(Mutation<StartInput>("Ship", typeof(ShipV1)));
        var after = await OneAsync(Mutation<StartInput>("Ship", typeof(ShipV2)));

        after.Slot.Should().Be(before.Slot);
        after
            .Id.Should()
            .NotBe(
                before.Id,
                "a type is left out of the shape because a namespace field reaches it, not because of its name"
            );
    }

    [Test]
    public async Task One_schema_writes_the_same_documents()
    {
        var first = await OneAsync(Mutation<StartInput>("StartRun", typeof(RunOutput)));
        var second = await OneAsync(Mutation<StartInput>("StartRun", typeof(RunOutput)));

        second.Should().BeEquivalentTo(first, o => o.Excluding(d => d.Registration));
    }

    #endregion

    #region Fixtures

    public record StartInput
    {
        public string Scenario { get; init; } = "";
    }

    public record RunOutput
    {
        public string RunId { get; init; } = "";
        public Step[] Steps { get; init; } = [];
    }

    public record Step
    {
        public string Name { get; init; } = "";
        public bool Done { get; init; }
    }

    [GraphQLName("ShipDispatchMutations")]
    public record ShipV1
    {
        public string Name { get; init; } = "";
    }

    [GraphQLName("ShipDispatchMutations")]
    public record ShipV2
    {
        public string Name { get; init; } = "";
        public int Crates { get; init; }
    }

    public record Node
    {
        public string Name { get; init; } = "";
        public Node[] Children { get; init; } = [];
    }

    public record Parent
    {
        public string Name { get; init; } = "";
        public Child[] Children { get; init; } = [];
    }

    public record Child
    {
        public string Name { get; init; } = "";
        public Parent? Parent { get; init; }
    }

    public record PagedParent
    {
        public string Name { get; init; } = "";

        [UsePaging]
        public PagedChild[] Children { get; init; } = [];
    }

    public record PagedChild
    {
        public string Name { get; init; } = "";
        public PagedParent? Parent { get; init; }
    }

    public record Order
    {
        public Address Billing { get; init; } = new();
        public Address Shipping { get; init; } = new();
    }

    public record Address
    {
        public string City { get; init; } = "";
    }

    public record Lookup
    {
        public string Name { get; init; } = "";

        public int Find(int key) => key;
    }

    [GraphQLName("ShapeInput")]
    public record ShapeInputV1
    {
        public string Name { get; init; } = "";
    }

    [GraphQLName("ShapeInput")]
    public record ShapeInputV2
    {
        public string Name { get; init; } = "";
        public int Copies { get; init; }
    }

    [GraphQLName("ShapeInput")]
    [GraphQLDescription("A described input.")]
    public record ShapeInputDescribed
    {
        [GraphQLDescription("The name.")]
        public string Name { get; init; } = "";
    }

    private static TrainRegistration Mutation<TInput>(
        string name,
        Type output,
        GraphQLOperation operations = GraphQLOperation.Run | GraphQLOperation.Queue
    ) => Registration<TInput>(name, output, operations, isQuery: false, graphqlNamespace: null);

    private static TrainRegistration Query<TInput>(
        string name,
        Type output,
        string? graphqlNamespace = null
    ) => Registration<TInput>(name, output, GraphQLOperation.Run, isQuery: true, graphqlNamespace);

    private static TrainRegistration Registration<TInput>(
        string name,
        Type output,
        GraphQLOperation operations,
        bool isQuery,
        string? graphqlNamespace
    ) =>
        new()
        {
            ServiceType = typeof(IStubTrain),
            ImplementationType = typeof(StubTrain),
            InputType = typeof(TInput),
            OutputType = output,
            Lifetime = ServiceLifetime.Scoped,
            ServiceTypeName = name,
            ImplementationTypeName = name,
            InputTypeName = typeof(TInput).Name,
            OutputTypeName = output.Name,
            RequiredPolicies = [],
            RequiredRoles = [],
            IsQuery = isQuery,
            IsMutation = !isQuery,
            IsBroadcastEnabled = false,
            GraphQLName = name,
            GraphQLOperations = operations,
            GraphQLNamespace = graphqlNamespace,
            IsRemote = false,
        };

    private static async Task<TrainDocument> OneAsync(TrainRegistration registration) =>
        (await GenerateAsync(registration)).Single();

    private static async Task<IReadOnlyList<TrainDocument>> GenerateAsync(
        params TrainRegistration[] registrations
    )
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITrainDiscoveryService>(new StubDiscovery(registrations));
        services.AddSingleton<TrainTypeModule>();
        var gql = services
            .AddGraphQLServer("trax")
            .AddQueryType(d =>
                d.Name("RootQuery").Field("_ping").Type<StringType>().Resolve("pong")
            )
            .AddTypeModule<TrainTypeModule>();
        if (registrations.Any(r => r.IsMutation))
            gql.AddMutationType<RootMutation>()
                .AddTypeExtension(
                    new ObjectTypeExtension(d =>
                        d.Name("RootMutation").Field("_ping").Type<StringType>().Resolve("pong")
                    )
                );

        var executor = await services
            .BuildServiceProvider()
            .GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax");
        return TrainDocuments.Generate(
            executor.Schema,
            TrainTypeModule.AssignTrainNames(registrations)
        );
    }

    private sealed class StubDiscovery(IReadOnlyList<TrainRegistration> registrations)
        : ITrainDiscoveryService
    {
        public IReadOnlyList<TrainRegistration> DiscoverTrains() => registrations;
    }

    private interface IStubTrain;

    private sealed class StubTrain;

    #endregion
}
