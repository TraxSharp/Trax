using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Trax.Effect.StateMachine;
using Trax.Samples.Recovery.Browser.Factories;
using Trax.Samples.Recovery.Browser.Fixtures;
using Trax.Samples.Recovery.E2E.Utilities;
using Trax.Samples.Shared.Testing;
using static Microsoft.Playwright.Assertions;

namespace Trax.Samples.Recovery.Browser.Tests;

/// <summary>
/// The topic map wizard driven from a browser, the proof that a machine is served as a typed API: the page
/// starts its instance on the server, sends declared events, and learns of every transition the server makes
/// (the build's outcome, the review timer, a recovery after a crash) by push, with no polling request.
/// </summary>
/// <remarks>
/// Each test opens a browser context of its own, so its draft is its own. Waits are on locators, requests and
/// the database (Trax.Docs/adr/0006), bounded by <see cref="BrowserTimeouts"/>.
/// </remarks>
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
[Property(
    "adr",
    "Trax.Docs/adr/0049-browser-tests-use-playwright-for-dotnet-in-test-projects-only.md"
)]
[TestFixture]
public class TopicMapBrowserTests
{
    private const string Adr = "Trax.Docs/adr/0048";

    private const string ViewerKey = "recovery-viewer-key-do-not-use-in-production";

    // The page labels each state; Expired is the state the built map's review timer moves it to.
    private const string ChoosingFields = "1. Fields";
    private const string ChoosingRange = "2. Years";
    private const string Building = "3. Building";
    private const string Built = "Built";
    private const string BuildFailed = "Build failed";
    private const string Expired = "Expired";

    [Test]
    public async Task TheWizard_RunsEndToEnd_WithAdHocDocumentsRefusedAndGenericMutationsOff()
    {
        await using var open = await BrowserSetup.NewPageAsync();
        var page = open.Recovery;
        await page.OpenTheWizardAsync();

        await ChooseFieldsAsync(page);
        await BuildAsync(page);
        await Expect(page.WizardState)
            .ToHaveTextAsync(Built, new() { Timeout = BrowserTimeouts.Server.Ms() });

        page.RequestsNaming("stateMachine")
            .Should()
            .BeEmpty(
                $"the page reaches the machine through its own typed operations ({Adr}), "
                    + "and the generic mutations are not exposed"
            );

        // The host runs only its persisted documents: an ad-hoc document is refused before it is validated, with the
        // persisted-operation code, whether it asks for the schema or for a generic mutation.
        var adHoc = await PostAsync(
            page,
            DemoKeysOperator,
            """{ __type(name: "DispatchMutations") { fields { name } } }"""
        );
        ErrorCode(adHoc)
            .Should()
            .Be(
                PersistedOperationRequired,
                "the host serves only its persisted documents to the page"
            );
        var generic = await PostAsync(
            page,
            DemoKeysOperator,
            """mutation { dispatch { stateMachine { __typename } } }"""
        );
        ErrorCode(generic)
            .Should()
            .Be(PersistedOperationRequired, "an ad-hoc generic mutation is refused like any other");

        // And the schema has no generic mutation to persist a document for: the operator's own registered
        // document naming one fails validation on the field, not on the transport.
        var upload = await UploadDocumentAsync(
            $"browser-generic-{Guid.NewGuid():N}",
            "mutation { dispatch { stateMachine { __typename } } }"
        );
        var refused = JsonDocument
            .Parse(upload)
            .RootElement.GetProperty("data")
            .GetProperty("persistedOperations")
            .GetProperty("uploadPersistedOperation");
        refused.GetProperty("success").GetBoolean().Should().BeFalse(upload);
        var error = refused.GetProperty("errors").EnumerateArray().Single();
        error.GetProperty("code").GetString().Should().Be("SCHEMA_VALIDATION_FAILED", upload);
        error
            .GetProperty("message")
            .GetString()
            .Should()
            .Be(
                "The field `stateMachine` does not exist on the type `DispatchMutations`.",
                "the generic mutations are off: nothing exposes them"
            );
    }

    [Test]
    public async Task TheBuildOutcome_ArrivesByPush_WithNoPollingRequest()
    {
        await using var open = await BrowserSetup.NewPageAsync();
        var page = open.Recovery;
        await page.OpenTheWizardAsync();
        await ChooseFieldsAsync(page);

        await BuildAsync(page);
        var afterBuild = page.GraphQLRequests.Count;
        await Expect(page.WizardState)
            .ToHaveTextAsync(Built, new() { Timeout = BrowserTimeouts.Server.Ms() });

        // Built's review timer: the push that entered Built carries its due time beside the snapshot.
        await Expect(page.ReviewDeadline)
            .ToBeVisibleAsync(new() { Timeout = BrowserTimeouts.Server.Ms() });
        var shown = DateTimeOffset.Parse(
            (await page.ReviewDeadline.GetAttributeAsync("data-due-at"))!,
            System.Globalization.CultureInfo.InvariantCulture
        );
        var held = await DraftRows.DueAtAsync((await DraftAsync(page)).Id);
        held.Should().NotBeNull("Built has a review timer, so the row holds its due time");
        shown
            .Should()
            .BeCloseTo(
                held!.Value,
                TimeSpan.FromMilliseconds(1),
                $"the page shows the due time the server holds, pushed beside the snapshot ({Adr})"
            );

        page.GraphQLRequests.Skip(afterBuild)
            .Select(r => r.PostData)
            .Should()
            .BeEmpty(
                $"the build's outcome and its review deadline are pushed to the page by topicMapChanged ({Adr}): "
                    + "it makes no request between the Build event's answer and rendering them"
            );
    }

    [Test]
    public async Task TheReviewTimer_ExpiresByPush()
    {
        await using var open = await BrowserSetup.NewPageAsync();
        var page = open.Recovery;
        await page.OpenTheWizardAsync();
        await ChooseFieldsAsync(page);
        await BuildAsync(page);
        await Expect(page.WizardState)
            .ToHaveTextAsync(Built, new() { Timeout = BrowserTimeouts.Server.Ms() });
        var draft = await DraftAsync(page);
        var beforeTheTimer = page.GraphQLRequests.Count;

        await DraftRows.MakeTheTimerDueAsync(draft.Id);

        await Expect(page.WizardState)
            .ToHaveTextAsync(Expired, new() { Timeout = BrowserTimeouts.Server.Ms() });
        page.GraphQLRequests.Skip(beforeTheTimer)
            .Should()
            .BeEmpty(
                $"the server fires the timer and pushes the move ({Adr}); the page neither fires it "
                    + "nor asks"
            );
    }

    [Test]
    public async Task AViewersUserOnlyEvent_IsRefusedOnTheServer()
    {
        await using var open = await BrowserSetup.NewPageAsync();
        var page = open.Recovery;
        await page.OpenTheWizardAsync();

        // The page offers a viewer no Build; the request is forced past it, as a hand-written client would.
        var started = await TypedAsync(
            page,
            ViewerKey,
            "start",
            new { requestId = $"viewer-start-{Guid.NewGuid():N}" }
        );
        var id = started.GetProperty("id").GetString()!;
        await TypedAsync(
            page,
            ViewerKey,
            "chooseFields",
            new
            {
                id,
                input = new { fields = new[] { "Hydrology" } },
                requestId = "viewer-fields",
            }
        );
        var revision = await DraftRows.RevisionAsync(Guid.Parse(id));

        var refused = await TypedAsync(
            page,
            ViewerKey,
            "build",
            new
            {
                id,
                input = new { fromYear = 2016, toYear = 2025 },
                requestId = "viewer-build",
            }
        );

        refused
            .GetProperty("problem")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("FORBIDDEN", $"Build requires recovery-user, checked on the server ({Adr})");
        (await DraftRows.RevisionAsync(Guid.Parse(id)))
            .Should()
            .Be(revision, "a refused event writes nothing");
    }

    [Test]
    public async Task TheDashboardAndGraphQL_ShowTheSameStateRevisionAndRuns()
    {
        await using var open = await BrowserSetup.NewPageAsync();
        var page = open.Recovery;
        await page.OpenTheWizardAsync();
        await ChooseFieldsAsync(page);
        var draft = await DraftAsync(page);

        // The dashboard's page for the draft records every run link it ever shows, from the first byte of each load:
        // a user's draft lists only the run its state waits on, which is gone again once the build ends.
        var dashboard = await open.Recovery.Context.NewPageAsync();
        await dashboard.AddInitScriptAsync(
            """
            window.__runLinks = new Set();
            const record = () => document
              .querySelectorAll(".cs-machine-runs a[href*='data/metadata/']")
              .forEach(a => window.__runLinks.add(a.getAttribute("href")));
            new MutationObserver(record).observe(document, { subtree: true, childList: true });
            """
        );
        var instancePage = new Uri(
            BrowserSetup.HostAddress,
            $"/trax/data/state-machines/topic-map/user/{draft.Id}?row={draft.RowId}"
        ).ToString();
        await dashboard.GotoAsync(instancePage);
        var runs = dashboard.Locator(".cs-machine-runs");
        await Expect(runs)
            .ToContainTextAsync("No runs to show", new() { Timeout = BrowserTimeouts.Render.Ms() });

        await BuildAsync(page);

        // Once the build's run has started, both read it: the dashboard loads the page again (it re-reads a
        // draft's runs when its state moves, and the state does not move when the queued run starts), and
        // GraphQL's operations.machineInstance is asked.
        (
            await Polling.WaitUntilAsync(
                async () => await DraftRows.RunStateAsync(draft.Id) == "in_progress",
                BrowserTimeouts.Server,
                TimeSpan.FromMilliseconds(25)
            )
        )
            .Should()
            .BeTrue("the build's run starts");
        var reloaded = dashboard.ReloadAsync();
        var live = await MachineInstanceAsync(draft);
        await reloaded;
        var run = live.GetProperty("invokedRuns").EnumerateArray().Single();
        run.GetProperty("isLive").GetBoolean().Should().BeTrue("the draft's state waits on it");
        var runId = run.GetProperty("id").GetInt64();
        (await DraftRows.LatestRunIdAsync(draft.Id))
            .Should()
            .Be(runId, "the run GraphQL lists is the one the build started");

        await Expect(page.WizardState)
            .ToHaveTextAsync(Built, new() { Timeout = BrowserTimeouts.Server.Ms() });
        (await dashboard.EvaluateAsync<string[]>("() => [...window.__runLinks]"))
            .Should()
            .ContainSingle(
                "the dashboard lists the same one live run while the map builds, per " + Adr
            )
            .Which.Should()
            .EndWith($"data/metadata/{runId}");

        // Built: the typed get, operations.machineInstance and the dashboard agree on the state, the revision,
        // and on there being no live run.
        var got = await TypedAsync(page, DemoKeysUser, "get", new { id = draft.Id });
        var revision = await DraftRows.RevisionAsync(draft.Id);
        got.GetProperty("state").GetString().Should().Be("Built");
        got.GetProperty("revision").GetInt64().Should().Be(revision);
        var built = await MachineInstanceAsync(draft);
        built.GetProperty("state").GetString().Should().Be("Built");
        built.GetProperty("revision").GetInt64().Should().Be(revision);
        built
            .GetProperty("invokedRuns")
            .GetArrayLength()
            .Should()
            .Be(0, "the build's run has ended");

        var card = dashboard.Locator(".rz-card").First;
        await Expect(card)
            .ToContainTextAsync("Built", new() { Timeout = BrowserTimeouts.Render.Ms() });
        await Expect(card)
            .ToContainTextAsync(
                new Regex($@"Revision\s*{revision}\b"),
                new() { Timeout = BrowserTimeouts.Render.Ms() }
            );
        await Expect(runs)
            .ToContainTextAsync("No runs to show", new() { Timeout = BrowserTimeouts.Render.Ms() });
    }

    [Test]
    public async Task ReloadingTheTab_ResumesTheServerMintedInstance()
    {
        await using var open = await BrowserSetup.NewPageAsync();
        var page = open.Recovery;
        await page.OpenTheWizardAsync();
        await ChooseFieldsAsync(page);
        var shown = await page.DraftId.TextContentAsync();

        await page.Page.ReloadAsync();
        await RecoveryPage.ShowTheTopicMapAsync(page.Page);

        await Expect(page.WizardState)
            .ToHaveTextAsync(ChoosingRange, new() { Timeout = BrowserTimeouts.Render.Ms() });
        await Expect(page.DraftId).ToHaveTextAsync(shown!);
        page.RequestsNaming("saveSnapshot")
            .Should()
            .BeEmpty(
                $"the server mints the instance id through start ({Adr}); the page writes none"
            );
        // The page sends start by its persisted id, trax_machine_topic-map_start_<hash>.
        page.RequestsNaming("trax_machine_topic-map_start_")
            .Should()
            .ContainSingle("one start, then the reload resumes it");
    }

    [Test]
    public async Task TwoTabs_SeeEachOthersTransitionsInRevisionOrder()
    {
        await using var open = await BrowserSetup.NewPageAsync();
        var first = open.Recovery;
        await first.OpenTheWizardAsync();
        // A second tab of the same browser holds the same draft.
        var second = await first.Context.NewPageAsync();
        await second.GotoAsync(RecoveryPage.PageOrigin + "/");
        await RecoveryPage.ShowTheTopicMapAsync(second);
        var secondState = second.Locator(".wizard-state");
        await Expect(secondState)
            .ToHaveTextAsync(ChoosingFields, new() { Timeout = BrowserTimeouts.Render.Ms() });

        await ChooseFieldsAsync(first);
        await Expect(secondState)
            .ToHaveTextAsync(ChoosingRange, new() { Timeout = BrowserTimeouts.Render.Ms() });
        await first
            .Page.GetByRole(AriaRole.Button, new() { Name = "Back", Exact = true })
            .ClickAsync();
        await Expect(first.WizardState)
            .ToHaveTextAsync(ChoosingFields, new() { Timeout = BrowserTimeouts.Render.Ms() });

        await Expect(secondState)
            .ToHaveTextAsync(ChoosingFields, new() { Timeout = BrowserTimeouts.Render.Ms() });
        var draft = await DraftAsync(first);
        (await DraftRows.RevisionAsync(draft.Id))
            .Should()
            .Be(3, $"start, ChooseFields and Back are three writes, each pushed in order ({Adr})");
    }

    [Test]
    public async Task ADraftTheServerNoLongerHolds_IsForgotten_AndANewOneStarted()
    {
        await using var open = await BrowserSetup.NewPageAsync();
        var page = open.Recovery;
        await page.OpenTheWizardAsync();
        await ChooseFieldsAsync(page);
        var draft = await DraftAsync(page);
        var shown = await page.DraftId.TextContentAsync();

        // Discarded from elsewhere: the page learns of it by push, as gone.
        var discarded = await TypedAsync(
            page,
            DemoKeysUser,
            "discard",
            new { id = draft.Id, requestId = $"elsewhere-{Guid.NewGuid():N}" }
        );
        discarded.GetProperty("discarded").GetBoolean().Should().BeTrue();

        await Expect(page.DraftId)
            .Not.ToHaveTextAsync(shown!, new() { Timeout = BrowserTimeouts.Render.Ms() });
        await Expect(page.WizardState)
            .ToHaveTextAsync(ChoosingFields, new() { Timeout = BrowserTimeouts.Render.Ms() });
        var started = await DraftAsync(page);
        started.Id.Should().NotBe(draft.Id, "the page started a new draft for the one it lost");

        // The new draft is the one the page keeps: a reload opens it rather than starting another.
        var starts = page.RequestsNaming("trax_machine_topic-map_start_").Count;
        await page.Page.ReloadAsync();
        await RecoveryPage.ShowTheTopicMapAsync(page.Page);
        await Expect(page.DraftId)
            .ToHaveTextAsync(
                $"draft {started.Id.ToString()[..8]}",
                new() { Timeout = BrowserTimeouts.Render.Ms() }
            );
        page.RequestsNaming("trax_machine_topic-map_start_")
            .Should()
            .HaveCount(starts, "the reload opens the kept draft");
    }

    [Test]
    public async Task StartingANewMap_DiscardsTheOldDraft()
    {
        await using var open = await BrowserSetup.NewPageAsync();
        var page = open.Recovery;
        await page.OpenTheWizardAsync();
        await ChooseFieldsAsync(page);
        var old = await DraftAsync(page);
        var shown = await page.DraftId.TextContentAsync();

        await page.Page.GetByRole(AriaRole.Button, new() { Name = "Start a new map" }).ClickAsync();

        await Expect(page.DraftId)
            .Not.ToHaveTextAsync(shown!, new() { Timeout = BrowserTimeouts.Render.Ms() });
        await Expect(page.WizardState)
            .ToHaveTextAsync(ChoosingFields, new() { Timeout = BrowserTimeouts.Render.Ms() });
        (await DraftRows.ExistsAsync(old.Id))
            .Should()
            .BeFalse("the old draft is discarded, not left to count against the demo user's cap");
        page.RequestsNaming("trax_machine_topic-map_start_")
            .Should()
            .HaveCount(2, "one start for the first draft and one for the new map");
    }

    [Test]
    public async Task StartingANewMapFromBuilt_ResetsTheDraft_AndLeavesNoneBehind()
    {
        await using var open = await BrowserSetup.NewPageAsync();
        var page = open.Recovery;
        await page.OpenTheWizardAsync();
        await ChooseFieldsAsync(page);
        await BuildAsync(page);
        await Expect(page.WizardState)
            .ToHaveTextAsync(Built, new() { Timeout = BrowserTimeouts.Server.Ms() });
        var draft = await DraftAsync(page);
        var shown = await page.DraftId.TextContentAsync();
        (await DraftRows.DueAtAsync(draft.Id)).Should().NotBeNull("Built holds a review timer");

        // The page shows the twin's prediction at once; the row is checked once the server answered.
        var answered = page.Page.WaitForResponseAsync(r =>
            r.Url.StartsWith(RecoveryPage.GraphQLUrl, StringComparison.Ordinal)
            && (r.Request.PostData ?? "").Contains(
                "trax_machine_topic-map_startOver_",
                StringComparison.Ordinal
            )
        );
        await page.Page.GetByRole(AriaRole.Button, new() { Name = "Start a new map" }).ClickAsync();
        await answered;

        await Expect(page.WizardState)
            .ToHaveTextAsync(ChoosingFields, new() { Timeout = BrowserTimeouts.Render.Ms() });
        await Expect(page.DraftId)
            .ToHaveTextAsync(shown!, new() { Timeout = BrowserTimeouts.Render.Ms() });
        await Expect(page.Page.Locator(".hint.error")).ToHaveCountAsync(0);
        (await DraftRows.FindAsync(draft.Id.ToString())).State.Should().Be("ChoosingFields");
        (await DraftRows.DueAtAsync(draft.Id))
            .Should()
            .BeNull(
                "leaving Built cancels the review timer, so it no longer keeps the draft alive"
            );
        page.RequestsNaming("trax_machine_topic-map_start_")
            .Should()
            .HaveCount(
                1,
                "the built draft, which the server will not discard, is reset rather than left behind "
                    + "to count against the demo user's cap beside a new one"
            );
        page.RequestsNaming("trax_machine_topic-map_startOver_").Should().HaveCount(1);
    }

    [Test]
    public async Task ADeployWithANewIR_ShowsClientOutdated()
    {
        // The page as it was built before a deploy changed the machine: the same code, an older IR hash.
        var hash = CommittedIrHash();
        var older = new string(hash.Reverse().ToArray());
        await using var open = await BrowserSetup.NewPageAsync(
            replace: (_, js) => js.Replace(hash, older, StringComparison.Ordinal)
        );
        var page = open.Recovery;
        await page.Page.GotoAsync(RecoveryPage.PageOrigin + "/");
        await RecoveryPage.ShowTheTopicMapAsync(page.Page);

        await Expect(page.Page.GetByText("CLIENT_OUTDATED"))
            .ToBeVisibleAsync(new() { Timeout = BrowserTimeouts.Render.Ms() });
        var afterRefusal = page.GraphQLRequests.Count;
        await Expect(page.Page.GetByRole(AriaRole.Button, new() { Name = "Next" }))
            .Not.ToBeVisibleAsync(new() { Timeout = BrowserTimeouts.Render.Ms() });
        page.GraphQLRequests.Count.Should()
            .Be(afterRefusal, $"an outdated client stops and makes no further requests ({Adr})");
    }

    [Test]
    public async Task KillingTheHostMidBuild_RecoversThroughTheStuckRunPath_AndThePageFollows()
    {
        await using var open = await BrowserSetup.NewPageAsync();
        var page = open.Recovery;
        await page.OpenTheWizardAsync();
        await ChooseFieldsAsync(page);
        await BuildAsync(page);
        var draft = await DraftAsync(page);
        (
            await Polling.WaitUntilAsync(
                async () => await DraftRows.RunStateAsync(draft.Id) == "in_progress",
                BrowserTimeouts.Server,
                TimeSpan.FromMilliseconds(100)
            )
        )
            .Should()
            .BeTrue("the build should be running before its host dies");

        await BrowserSetup.Host.KillAsync();
        var afterTheCrash = page.GraphQLRequests.Count;
        await BrowserSetup.RestartHostAsync();

        await Expect(page.WizardState)
            .ToHaveTextAsync(BuildFailed, new() { Timeout = BrowserTimeouts.Server.Ms() });
        (await DraftRows.RunStateAsync(draft.Id)).Should().Be("failed", "the new host reaped it");
        page.GraphQLRequests.Skip(afterTheCrash)
            .Select(r => r.PostData)
            .Should()
            .BeEmpty(
                $"the page's subscription reconnects and the recovery's write is pushed to it ({Adr}); "
                    + "it does not poll"
            );
    }

    private const string DemoKeysOperator = "recovery-operator-key-do-not-use-in-production";

    // The page signs in as the demo user, so its drafts are the user's: only the user's key reads or discards them.
    private const string DemoKeysUser = "recovery-user-key-do-not-use-in-production";

    private static async Task ChooseFieldsAsync(RecoveryPage page)
    {
        await page.Page.GetByRole(AriaRole.Button, new() { Name = "Next" }).ClickAsync();
        await Expect(page.WizardState)
            .ToHaveTextAsync(ChoosingRange, new() { Timeout = BrowserTimeouts.Render.Ms() });
    }

    private static async Task BuildAsync(RecoveryPage page)
    {
        var answered = page.Page.WaitForResponseAsync(r =>
            r.Url.StartsWith(RecoveryPage.GraphQLUrl, StringComparison.Ordinal)
            && (r.Request.PostData ?? "").Contains(
                "trax_machine_topic-map_build_",
                StringComparison.Ordinal
            )
        );
        await page.Page.GetByRole(AriaRole.Button, new() { Name = "Build the map" }).ClickAsync();
        await answered;
        await Expect(page.WizardState)
            .ToHaveTextAsync(
                new Regex($"^({Regex.Escape(Building)}|{Built})$"),
                new() { Timeout = BrowserTimeouts.Render.Ms() }
            );
    }

    private static async Task<(Guid Id, long RowId, string State)> DraftAsync(RecoveryPage page)
    {
        var shown = (await page.DraftId.TextContentAsync())!;
        return await DraftRows.FindAsync(shown.Replace("draft", "").Trim());
    }

    // A request the page's own code would not make, sent from inside the page.
    private static async Task<JsonElement> PostAsync(RecoveryPage page, string key, string query)
    {
        var body = await page.Page.EvaluateAsync<string>(
            """
            async ([url, key, query]) => {
              const response = await fetch(url, {
                method: "POST",
                headers: { "content-type": "application/json", "X-Api-Key": key },
                body: JSON.stringify({ query }),
              });
              return await response.text();
            }
            """,
            new object[] { RecoveryPage.GraphQLUrl, key, query }
        );
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    // One operation on the topic map's typed surface (the wire contract in Trax.Docs/adr/0048), sent from the page
    // by the persisted id the host's manifest lists for it, with its values as variables: the host runs nothing else.
    private static async Task<JsonElement> TypedAsync(
        RecoveryPage page,
        string key,
        string operation,
        object variables
    )
    {
        var body = await page.Page.EvaluateAsync<string>(
            """
            async ([url, key, request]) => {
              const response = await fetch(url, {
                method: "POST",
                headers: { "content-type": "application/json", "X-Api-Key": key },
                body: request,
              });
              return await response.text();
            }
            """,
            new object[]
            {
                RecoveryPage.GraphQLUrl,
                key,
                JsonSerializer.Serialize(
                    new { id = await DocumentIdAsync(operation), variables },
                    JsonSerializerOptions.Web
                ),
            }
        );
        var response = JsonDocument.Parse(body).RootElement.Clone();
        var field = operation;
        response
            .TryGetProperty("errors", out var errors)
            .Should()
            .BeFalse($"the topic map is served as a typed API ({Adr}): {errors}");
        return response
            .GetProperty("data")
            .GetProperty("machines")
            .GetProperty("topicMap")
            .GetProperty(field);
    }

    // The id of the topic map's document for an operation, from the committed client manifest the page is built from.
    private static async Task<string> DocumentIdAsync(string operation) =>
        ClientManifest
            .Read(await File.ReadAllTextAsync(TopicMapArtifacts.ClientManifest))
            .Machines.Single(m => m.Id == "topic-map")
            .Documents.Single(d => d.Operation == operation)
            .Id;

    private static GraphQLClient GraphQLForTest() => new(BrowserSetup.Host.Http);

    private const string PersistedOperationRequired = "PERSISTED_OPERATION_REQUIRED";

    private static string? ErrorCode(JsonElement response) =>
        response.TryGetProperty("errors", out var errors)
        && errors[0].TryGetProperty("extensions", out var extensions)
        && extensions.TryGetProperty("code", out var code)
            ? code.GetString()
            : null;

    private const string MachineInstanceDocument = """
        query TopicMapInstance($id: UUID!, $rowId: Long!) {
          operations {
            machineInstance(machine: "topic-map", ownerKind: USER, id: $id, rowId: $rowId) {
              state revision invokedRuns { id isLive }
            }
          }
        }
        """;

    // operations.machineInstance for the draft, through a document the operator registers on the host's management
    // surface: the host runs no ad-hoc document, and the page has no document for the operations view of a draft.
    private static async Task<JsonElement> MachineInstanceAsync(
        (Guid Id, long RowId, string State) draft
    )
    {
        const string id = "browser-test-topic-map-instance-v1";
        var upload = await UploadDocumentAsync(id, MachineInstanceDocument);
        upload.Should().Contain("\"success\":true", upload);
        using var response = await BrowserSetup.Host.Http.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, "/trax/graphql")
            {
                Content = JsonContent.Create(
                    new { id, variables = new { id = draft.Id, rowId = draft.RowId } }
                ),
                Headers = { { "X-Api-Key", DemoKeysOperator } },
            }
        );
        var root = JsonDocument
            .Parse(await response.Content.ReadAsStringAsync())
            .RootElement.Clone();
        root.TryGetProperty("errors", out var errors).Should().BeFalse(errors.ToString());
        return root.GetProperty("data").GetProperty("operations").GetProperty("machineInstance");
    }

    // Registers a document through persistedOperations, whose management gate admits the operator;
    // returns the whole answer.
    private static async Task<string> UploadDocumentAsync(string id, string document)
    {
        var response = await GraphQLForTest()
            .SendAsync(
                """
                mutation Upload($input: UploadPersistedOperationInput!) {
                  persistedOperations {
                    uploadPersistedOperation(input: $input) { success errors { code message } }
                  }
                }
                """,
                DemoKeysOperator,
                new { input = new { id, document } }
            );
        return response.Root.GetRawText();
    }

    private static string CommittedIrHash()
    {
        var twin = File.ReadAllText(
            Path.Combine(
                RecoveryClientBuild.ClientFolder,
                "src",
                "trax",
                "topic-map",
                "topic-map.machine.g.ts"
            )
        );
        return Regex.Match(twin, "irHash = \"(?<hash>[0-9a-f]{64})\"").Groups["hash"].Value;
    }
}
