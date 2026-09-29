using System.Text.Json;
using FluentAssertions;
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Api.Tests.Auth;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Api.Tests;

/// <summary>
/// A train input is carried by the single-row detail reads only, never by a list. Enforces
/// <c>docs/adr/0016-a-train-input-is-read-one-row-at-a-time.md</c>: an execution's input,
/// a manifest's properties and a work queue entry's input can hold credentials, and a list
/// that returned them would hand every row's input to whoever pages through it.
/// </summary>
[Property("adr", "docs/adr/0016-a-train-input-is-read-one-row-at-a-time.md")]
[TestFixture]
public class TrainInputReadsTests
{
    private ServiceProvider _provider = null!;
    private IRequestExecutor _executor = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TraxMarker>();
        var discovery = Substitute.For<ITrainDiscoveryService>();
        discovery.DiscoverTrains().Returns([]);
        services.AddSingleton(discovery);
        services.AddSingleton(Substitute.For<IEffectRegistry>());
        Trax.Api.GraphQL.Extensions.GraphQLServiceExtensions.AddTraxGraphQL(
            services,
            graphql => graphql.ExposeOperationQueries().AllowAnonymousOperations()
        );
        _provider = services.BuildServiceProvider();
        _executor = await _provider
            .GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax");
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown() => await _provider.DisposeAsync();

    [TestCase("ExecutionSummary", "input")]
    [TestCase("ManifestSummary", "properties")]
    [TestCase("WorkQueueSummary", "input")]
    public void ListTypes_CarryNoTrainInput(string typeName, string field)
    {
        FieldsOf(typeName)
            .Should()
            .NotContain(
                field,
                "a train input is read one row at a time, on the detail type "
                    + "(docs/adr/0016-a-train-input-is-read-one-row-at-a-time.md)"
            );
    }

    [TestCase("ExecutionDetail", "input")]
    [TestCase("ManifestDetail", "properties")]
    [TestCase("WorkQueueDetail", "input")]
    public void DetailTypes_CarryTheTrainInput(string typeName, string field)
    {
        FieldsOf(typeName)
            .Should()
            .Contain(
                field,
                "the detail read is where a train input is served "
                    + "(docs/adr/0016-a-train-input-is-read-one-row-at-a-time.md)"
            );
    }

    private IEnumerable<string> FieldsOf(string typeName) =>
        _executor
            .Schema.Types.OfType<ObjectType>()
            .Single(t => t.Name == typeName)
            .Fields.Select(f => f.Name);

    /// <summary>
    /// The reads that return a train input or an effect's settings, which can hold credentials.
    /// They sit under the operations namespace so they answer to the same gate an execution's
    /// input does, and nothing else.
    /// </summary>
    private const string InputBearingReads = """
        {
          operations {
            effects { configuration }
            manifestDetail(id: 1) { properties }
            workQueue { detail(id: 1) { input } }
            executionDetail(id: 1) { input }
          }
        }
        """;

    [TestCase(null)]
    [TestCase(AdminOperationsAuthorizationTests.ReaderApiKey)]
    public async Task NamespaceGatedByRole_InputBearingReads_AreRefusedWithoutTheRole(
        string? apiKey
    )
    {
        using var host = await AdminOperationsAuthorizationTests.StartHostAsync(
            AdminOperationsAuthorizationTests.Posture.NamespaceGatedByRole
        );

        var doc = await AdminOperationsAuthorizationTests.PostAsync(
            host,
            apiKey,
            InputBearingReads
        );

        AdminOperationsAuthorizationTests
            .HasErrorCode(doc, "TRAX_AUTHORIZATION")
            .Should()
            .BeTrue(
                "manifest properties, work queue input and effect settings share the gate on "
                    + "execution input (docs/adr/0016-a-train-input-is-read-one-row-at-a-time.md)"
            );
        var dataIsEmpty =
            !doc.RootElement.TryGetProperty("data", out var data)
            || data.ValueKind == JsonValueKind.Null
            || data.GetProperty("operations").ValueKind == JsonValueKind.Null;
        dataIsEmpty.Should().BeTrue("a refused caller must not receive any of it");

        await host.StopAsync();
    }

    [Test]
    public async Task NamespaceGatedByRole_RoleHolder_ReadsEffectsAndHostConfig()
    {
        using var host = await AdminOperationsAuthorizationTests.StartHostAsync(
            AdminOperationsAuthorizationTests.Posture.NamespaceGatedByRole
        );

        var doc = await AdminOperationsAuthorizationTests.PostAsync(
            host,
            AdminOperationsAuthorizationTests.AdminApiKey,
            "{ operations { effects { isConfigurable configuration } config { environmentName logLevels { category level } } } }"
        );

        doc.RootElement.TryGetProperty("errors", out var errors)
            .Should()
            .BeFalse(errors.ToString());
        var config = doc
            .RootElement.GetProperty("data")
            .GetProperty("operations")
            .GetProperty("config");
        config.GetProperty("environmentName").GetString().Should().NotBeNullOrEmpty();
        config.GetProperty("logLevels").ValueKind.Should().Be(JsonValueKind.Array);

        await host.StopAsync();
    }
}
