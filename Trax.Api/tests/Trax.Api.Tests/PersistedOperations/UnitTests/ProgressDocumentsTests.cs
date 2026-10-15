using AwesomeAssertions;
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.PersistedOperations.Services;
using Trax.Api.GraphQL.TypeModules;
using Trax.Effect.StateMachine;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Api.Tests.PersistedOperations.UnitTests;

/// <summary>
/// A host where a train lets its starter follow it registers two documents under <c>trax_progress_</c>, a first read
/// and a stream, whose ids read back to their slot; a host where none does registers none, and the client manifest
/// lists them and marks the trains they serve.
/// </summary>
[TestFixture]
[Property("adr", Adr)]
public class ProgressDocumentsTests
{
    private const string Adr =
        "Trax.Docs/adr/0051-only-the-caller-who-started-a-run-may-follow-its-progress.md";

    [Test]
    public async Task A_host_with_a_progress_train_has_a_query_and_a_subscription_document()
    {
        var documents = ProgressDocuments.Generate(await SchemaAsync(progress: true));

        documents.Select(d => d.Operation).Should().Equal("query", "subscription");
        documents[0]
            .Text.Should()
            .StartWith("query RunProgress($runId: String!) { runProgress(runId: $runId) {")
            .And.Contain("steps {")
            .And.Contain("problem { code message }");
        documents[1]
            .Text.Should()
            .StartWith(
                "subscription RunProgressEvents($runId: String!) { runProgress(runId: $runId) {"
            )
            .And.Contain("sequence");
        foreach (var document in documents)
        {
            ProgressDocuments.SlotOf(document.Id).Should().Be(document.Slot);
            GeneratedDocuments.IsReserved(document.Id).Should().BeTrue();
            GeneratedDocuments.SlotOf(document.Id, [document.Slot]).Should().Be(document.Slot);
            document.Matches(document.Text).Should().BeTrue();
            document.Matches(document.Text + " ").Should().BeFalse();
        }
    }

    [Test]
    public async Task A_host_without_one_has_none()
    {
        ProgressDocuments
            .Generate(await SchemaAsync(progress: false))
            .Should()
            .BeEmpty("runProgress exists only where a train opts in, per " + Adr);
    }

    [TestCase("trax_progress_query")]
    [TestCase("trax_progress_mutation_HASH")]
    [TestCase("trax_progress_query_extra_HASH")]
    [TestCase("trax_train_query__findOrder_HASH")]
    public void An_id_that_is_not_a_progress_document_names_no_slot(string id) =>
        ProgressDocuments
            .SlotOf(
                id.Replace(
                    "HASH",
                    new string('a', MachineDocuments.IrHashLength)
                        + new string('b', MachineDocuments.TextHashLength)
                )
            )
            .Should()
            .BeNull();

    [Test]
    public async Task The_manifest_lists_the_documents_and_marks_the_train()
    {
        var registration = Registration(progress: true);
        var schema = await SchemaAsync(registration);

        var manifest = ClientManifestExport.Build(
            schema,
            [],
            TrainTypeModule.AssignTrainNames([registration])
        );

        manifest.Progress.Select(d => d.Operation).Should().Equal("query", "subscription");
        manifest.Trains.Should().ContainSingle().Which.Progress.Should().BeTrue();
        manifest.Types.Should().Contain("type RunProgress").And.Contain("RunProgressEvent");
        ClientManifest.Read(ClientManifest.Write(manifest)).Should().BeEquivalentTo(manifest);
    }

    #region Fixtures

    public record StartInput
    {
        public string Name { get; init; } = "";
    }

    public record StartOutput
    {
        public string Id { get; init; } = "";
    }

    private static Task<HotChocolate.ISchemaDefinition> SchemaAsync(bool progress) =>
        SchemaAsync(Registration(progress));

    private static async Task<HotChocolate.ISchemaDefinition> SchemaAsync(
        TrainRegistration registration
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization();
        services.AddSingleton<ITrainDiscoveryService>(new StubDiscovery([registration]));
        services.AddSingleton<TrainTypeModule>();
        services.AddSingleton<RunProgressTypeModule>();
        var executor = await services
            .AddGraphQLServer("trax")
            .AddAuthorization()
            .AddQueryType(d =>
                d.Name("RootQuery").Field("_ping").Type<StringType>().Resolve("pong")
            )
            .AddMutationType<Trax.Api.GraphQL.Mutations.RootMutation>()
            .AddTypeExtension(
                new ObjectTypeExtension(d =>
                    d.Name("RootMutation").Field("_ping").Type<StringType>().Resolve("pong")
                )
            )
            .AddSubscriptionType(d =>
                d.Name(MachineChangedSubscription.SubscriptionType)
                    .Field("_ping")
                    .Type<StringType>()
                    .Resolve("pong")
            )
            .AddTypeModule<TrainTypeModule>()
            .AddTypeModule<RunProgressTypeModule>()
            .Services.BuildServiceProvider()
            .GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax");
        return executor.Schema;
    }

    private static TrainRegistration Registration(bool progress) =>
        new()
        {
            ServiceType = typeof(IStubTrain),
            ImplementationType = typeof(StubTrain),
            InputType = typeof(StartInput),
            OutputType = typeof(StartOutput),
            Lifetime = ServiceLifetime.Scoped,
            ServiceTypeName = "StartImport",
            ImplementationTypeName = "StartImport",
            InputTypeName = nameof(StartInput),
            OutputTypeName = nameof(StartOutput),
            RequiredPolicies = [],
            RequiredRoles = [],
            IsQuery = false,
            IsMutation = true,
            IsBroadcastEnabled = false,
            IsRunProgressEnabled = progress,
            GraphQLName = "StartImport",
            GraphQLOperations = Trax.Effect.Attributes.GraphQLOperation.Run,
            IsRemote = false,
        };

    private sealed class StubDiscovery(IReadOnlyList<TrainRegistration> registrations)
        : ITrainDiscoveryService
    {
        public IReadOnlyList<TrainRegistration> DiscoverTrains() => registrations;
    }

    private interface IStubTrain;

    private sealed class StubTrain;

    #endregion
}
