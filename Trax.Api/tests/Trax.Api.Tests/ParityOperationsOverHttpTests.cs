using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Trax.Api.Auth.ApiKey;
using Trax.Api.GraphQL.Extensions;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Services.Effects;
using Trax.Scheduler.Services.LogLevels;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests;

/// <summary>
/// The operations fields added to match the dashboard, over HTTP against a host that gates the
/// operations namespace to a role: each answers a role holder through the shared Scheduler
/// service, with the arguments and payload the documents below use, and each refuses a caller
/// without the role, because it sits under the gated <c>operations</c> field.
/// </summary>
[Property("adr", "docs/adr/0004-the-operations-namespace-gates-independently-of-the-endpoint.md")]
[TestFixture]
public class ParityOperationsOverHttpTests
{
    private const string AdminKey = "parity-admin-key";
    private const string ReaderKey = "parity-reader-key";

    private const string TriggerManifests =
        "mutation { operations { triggerManifests(ids: [1, 2], askAfresh: true) "
        + "{ success matched queued alreadyQueued tooLateToAskAfresh skipped message notes { id message } } } }";
    private const string TriggerGroups =
        "mutation { operations { triggerGroups(ids: [3]) { success matched queued message } } }";
    private const string CancelGroups =
        "mutation { operations { cancelGroups(ids: [3]) { success count message } } }";
    private const string ConfigureEffect =
        "mutation { operations { configureEffect(fullName: \"Sink\", values: "
        + "[{ name: \"BatchSize\", value: \"10\" }, { name: \"Target\", value: null }]) "
        + "{ success count message errors { field message } } } }";
    private const string SetLogLevels =
        "mutation { operations { config { setLogLevels(levels: [{ category: \"Trax\", level: DEBUG }]) "
        + "{ success count notApplied message } } } }";
    private const string Decisions =
        "{ operations { decisions(metadataId: 7, take: 10) { take nextCursor items { id questionKey "
        + "occurrence kind answer refused isRefused replayed replayRefused decidedAt answerWithheld trackWithheld } } } }";
    private const string Effects =
        "{ operations { effects { fullName fields { name typeName kind nullable enumValues sensitive hasValue value hint } } } }";
    private const string LogLevels =
        "{ operations { config { logLevels { category level configuredLevel overridden } version } } }";

    private IHost _host = null!;
    private IOperationsService _operations = null!;
    private IEffectSettingsService _effects = null!;
    private ILogLevelService _logLevels = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _operations = Substitute.For<IOperationsService>();
        _operations
            .TriggerManifestsAsync(
                Arg.Any<IReadOnlyCollection<long>>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                new BatchTriggerResult(
                    true,
                    1,
                    1,
                    0,
                    0,
                    1,
                    "1 queued",
                    [new BatchItemNote(2, "Manifest 2 not found.")]
                )
            );
        _operations
            .TriggerManifestGroupsAsync(
                Arg.Any<IReadOnlyCollection<long>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new BatchTriggerResult(true, 1, 4, 0, 0, 0, "4 queued", []));
        _operations
            .CancelManifestGroupsAsync(
                Arg.Any<IReadOnlyCollection<long>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new OperationResult(true, Count: 5, Message: "5 flagged"));
        _operations
            .GetRecordedDecisionsAsync(
                Arg.Any<long>(),
                Arg.Any<long?>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                new RecordedDecisionPage(
                    [
                        new RecordedDecisionRecord(
                            11,
                            7,
                            "Route",
                            0,
                            "choice",
                            "{}",
                            """{"value": "Express", "replay_refused": "state changed"}""",
                            null,
                            false,
                            "f",
                            null,
                            null,
                            false,
                            null,
                            null,
                            null,
                            DateTime.UtcNow,
                            false,
                            false
                        ),
                    ],
                    10,
                    11
                )
            );

        _effects = Substitute.For<IEffectSettingsService>();
        _effects
            .GetEffects()
            .Returns([
                new EffectSettings(
                    "Sink",
                    "Sink",
                    true,
                    true,
                    true,
                    "SinkSettings",
                    "{}",
                    [
                        new EffectSettingField(
                            "ApiKey",
                            "String",
                            EffectFieldKind.Text,
                            false,
                            null,
                            true,
                            true,
                            // A service that slipped would still not leak it.
                            "sk-live-123",
                            "text"
                        ),
                    ]
                ),
            ]);
        _effects
            .ConfigureEffect(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string?>>())
            .Returns(
                new EffectConfigurationResult(
                    false,
                    0,
                    "not saved",
                    new Dictionary<string, string> { ["BatchSize"] = "too big" }
                )
            );

        _logLevels = Substitute.For<ILogLevelService>();
        _logLevels
            .GetLogLevels()
            .Returns([new CategoryLogLevel("Default", LogLevel.Information, "Information", false)]);
        _logLevels
            .SetLogLevels(Arg.Any<IReadOnlyList<LogLevelChange>>())
            .Returns(new LogLevelUpdateResult(true, 1, [], "1 set"));

        _host = new HostBuilder()
            .ConfigureWebHost(web =>
                web.UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddLogging();
                        services.AddRouting();
                        services.AddTraxApiKeyAuth(keys =>
                            keys.Add(AdminKey, id: "admin", "Admin")
                                .Add(ReaderKey, id: "reader", "Reader")
                        );
                        services.AddSingleton<TraxMarker>();
                        var discovery = Substitute.For<ITrainDiscoveryService>();
                        discovery.DiscoverTrains().Returns([]);
                        services.AddSingleton(discovery);
                        services.AddSingleton(Substitute.For<IEffectRegistry>());
                        services.AddTraxGraphQL(graphql =>
                            graphql
                                .ExposeOperationQueries()
                                .ExposeOperationMutations()
                                .GateOperations(roles: "Admin")
                        );

                        services.AddScoped(_ => _operations);
                        services.AddScoped(_ => _effects);
                        services.AddSingleton(_logLevels);
                        services.AddScoped(_ => Substitute.For<ITraxScheduler>());
                        services.AddScoped(_ => Substitute.For<ITrainExecutionService>());
                        services.AddScoped(_ =>
                            Substitute.For<Trax.Scheduler.Services.JobSubmitter.IJobSubmitter>()
                        );
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseEndpoints(e => e.MapGraphQL("/trax/graphql", "trax"));
                    })
            )
            .Build();

        await _host.StartAsync();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _host.StopAsync();
        _host.Dispose();
    }

    private static IEnumerable<TestCaseData> Documents()
    {
        yield return new TestCaseData(TriggerManifests).SetName("triggerManifests");
        yield return new TestCaseData(TriggerGroups).SetName("triggerGroups");
        yield return new TestCaseData(CancelGroups).SetName("cancelGroups");
        yield return new TestCaseData(ConfigureEffect).SetName("configureEffect");
        yield return new TestCaseData(SetLogLevels).SetName("config.setLogLevels");
        yield return new TestCaseData(Decisions).SetName("decisions");
        yield return new TestCaseData(Effects).SetName("effects.fields");
        yield return new TestCaseData(LogLevels).SetName("config.logLevels+version");
    }

    [TestCaseSource(nameof(Documents))]
    public async Task WithoutTheRole_IsRefused(string document)
    {
        var doc = await PostAsync(document, ReaderKey);

        Auth.AdminOperationsAuthorizationTests.HasErrorCode(doc, "TRAX_AUTHORIZATION")
            .Should()
            .BeTrue(doc.RootElement.GetRawText());
        var noData =
            !doc.RootElement.TryGetProperty("data", out var data)
            || data.ValueKind == JsonValueKind.Null
            || data.GetProperty("operations").ValueKind == JsonValueKind.Null;
        noData.Should().BeTrue("a refused caller gets nothing from the namespace");
    }

    [TestCaseSource(nameof(Documents))]
    public async Task WithTheRole_Answers(string document)
    {
        var doc = await PostAsync(document, AdminKey);

        doc.RootElement.TryGetProperty("errors", out _)
            .Should()
            .BeFalse(doc.RootElement.GetRawText());
    }

    [Test]
    public async Task TriggerManifests_PassesTheIdsAndAskAfresh_AndReturnsTheNotes()
    {
        var payload = Operations(await PostAsync(TriggerManifests, AdminKey), "triggerManifests");

        payload.GetProperty("queued").GetInt32().Should().Be(1);
        payload.GetProperty("skipped").GetInt32().Should().Be(1);
        payload.GetProperty("notes")[0].GetProperty("id").GetInt64().Should().Be(2);
        await _operations
            .Received()
            .TriggerManifestsAsync(
                Arg.Is<IReadOnlyCollection<long>>(ids =>
                    ids != null && ids.SequenceEqual(new long[] { 1, 2 })
                ),
                true,
                Arg.Any<CancellationToken>()
            );
    }

    [Test]
    public async Task ConfigureEffect_SendsOnlyTheGivenSettings_NullsIncluded_AndListsTheErrors()
    {
        var payload = Operations(await PostAsync(ConfigureEffect, AdminKey), "configureEffect");

        payload.GetProperty("success").GetBoolean().Should().BeFalse();
        payload.GetProperty("errors")[0].GetProperty("field").GetString().Should().Be("BatchSize");
        _effects
            .Received()
            .ConfigureEffect(
                "Sink",
                Arg.Is<IReadOnlyDictionary<string, string?>>(v =>
                    v != null && v.Count == 2 && v["BatchSize"] == "10" && v["Target"] == null
                )
            );
    }

    [Test]
    public async Task Effects_NeverCarryASensitiveValue()
    {
        var doc = await PostAsync(Effects, AdminKey);

        var field = Operations(doc, "effects")[0].GetProperty("fields")[0];
        field.GetProperty("sensitive").GetBoolean().Should().BeTrue();
        field.GetProperty("value").ValueKind.Should().Be(JsonValueKind.Null);
        doc.RootElement.GetRawText().Should().NotContain("sk-live-123");
    }

    [Test]
    public async Task Decisions_CarryTheReplayRefusalFromTheAnswer()
    {
        var payload = Operations(await PostAsync(Decisions, AdminKey), "decisions");

        payload.GetProperty("nextCursor").GetInt64().Should().Be(11);
        payload
            .GetProperty("items")[0]
            .GetProperty("replayRefused")
            .GetString()
            .Should()
            .Be("state changed");
    }

    [Test]
    public async Task SetLogLevels_TakesTheLevelAsAnEnum()
    {
        await PostAsync(SetLogLevels, AdminKey);

        _logLevels
            .Received()
            .SetLogLevels(
                Arg.Is<IReadOnlyList<LogLevelChange>>(l =>
                    l != null && l.Count == 1 && l[0] == new LogLevelChange("Trax", LogLevel.Debug)
                )
            );
    }

    private static JsonElement Operations(JsonDocument doc, string field)
    {
        doc.RootElement.TryGetProperty("errors", out _)
            .Should()
            .BeFalse(doc.RootElement.GetRawText());
        return doc.RootElement.GetProperty("data").GetProperty("operations").GetProperty(field);
    }

    private async Task<JsonDocument> PostAsync(string query, string apiKey)
    {
        var client = _host.GetTestServer().CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/trax/graphql")
        {
            Content = JsonContent.Create(new { query }),
        };
        request.Headers.Add("X-Api-Key", apiKey);
        var response = await client.SendAsync(request);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
}
