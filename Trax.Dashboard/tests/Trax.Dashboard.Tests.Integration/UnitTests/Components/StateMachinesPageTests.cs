using AwesomeAssertions;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Trax.Api.GraphQL.Queries;
using Trax.Dashboard.Components.Pages.Data;
using Trax.Dashboard.Models;
using Trax.Dashboard.Tests.Integration.Fakes.Data;
using Trax.Dashboard.Tests.Integration.Fakes.Services;
using Trax.Dashboard.Utilities;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.SnapshotDraft;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// The State machines pages read instances through <see cref="IOperationsService"/>, the calls
/// behind the API's <c>machineInstances</c>, <c>machineInstance</c> and
/// <c>machineInstanceCounts</c>, so both surfaces show the same instances in the same order with
/// the same counts. Neither page shows an instance's context or the user who owns it.
/// </summary>
[TestFixture]
[Property("adr", "Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md")]
public class StateMachinesPageTests
{
    private const string Machine = "Acme.Fulfilment";
    private const string SecretContext = """{"cardNumber":"4111-1111-1111-1111"}""";
    private const string UserKey = "shopper@example.com";

    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);

    private Bunit.TestContext _ctx = null!;
    private InMemoryDataContextFactory _data = null!;

    [SetUp]
    public void SetUp()
    {
        _ctx = new Bunit.TestContext();
        _ctx.Services.AddRadzenComponents();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _data = new InMemoryDataContextFactory();
        _ctx.Services.AddDashboardPageServices(_data);
        _ctx.Services.AddSingleton(UnusedService<ITraxScheduler>.Create());
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    [Test]
    public async Task The_list_and_counts_are_the_ones_the_api_returns()
    {
        var at = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 3; i++)
            await SeedAsync(SnapshotOwnerKind.System, "Running", at.AddMinutes(i));
        await SeedAsync(SnapshotOwnerKind.User, "Draft", at.AddMinutes(10));

        var page = _ctx.RenderComponent<StateMachinesPage>();

        using var scope = _ctx.Services.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<IOperationsService>();
        var api = await new OperationsQueries().GetMachineInstances(operations, default);
        var apiCounts = await new OperationsQueries().GetMachineInstanceCounts(operations, default);

        page.WaitForAssertion(
            () =>
            {
                var rows = page.FindAll("tr.rz-data-row").Select(r => r.TextContent).ToList();
                // The counts grid's three rows come first, then one row per instance.
                rows.Should().HaveCount(apiCounts.Count + api.Items.Count);
                rows.Skip(apiCounts.Count)
                    .Select((text, i) => text.Contains(api.Items[i].Id.ToString()))
                    .Should()
                    .OnlyContain(found => found, "the grid lists the API's page in its order");
                rows.Take(apiCounts.Count)
                    .Zip(apiCounts)
                    .Should()
                    .OnlyContain(pair =>
                        pair.First.Contains(pair.Second.State)
                        && pair.First.Contains(pair.Second.Count.ToString())
                    );
            },
            WaitTimeout
        );
    }

    [Test]
    public async Task The_grid_reads_the_page_and_total_the_api_reads_for_the_same_filter()
    {
        var at = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 7; i++)
            await SeedAsync(
                SnapshotOwnerKind.System,
                i % 2 == 0 ? "Running" : "Done",
                at.AddSeconds(i)
            );

        using var scope = _ctx.Services.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<IOperationsService>();
        var grid = await MachineInstanceGridQuery.LoadPageAsync(
            operations,
            new MachineInstanceQuery(Machine, "Running", SnapshotOwnerKind.System),
            new LoadDataArgs { Skip = 1, Top = 2 },
            new GridCount(),
            new MachineInstanceCountCapped(),
            default
        );
        var api = await new OperationsQueries().GetMachineInstances(
            operations,
            default,
            Machine,
            "Running",
            SnapshotOwnerKind.System,
            skip: 1,
            take: 2
        );

        grid.Items.Select(r => r.RowId).Should().Equal(api.Items.Select(i => i.RowId));
        grid.TotalCount.Should().Be(api.TotalCount).And.Be(4);
    }

    [Test]
    public async Task Neither_page_shows_a_context_or_the_owning_user()
    {
        var draft = await SeedAsync(
            SnapshotOwnerKind.User,
            "AwaitingPayment",
            DateTimeOffset.UtcNow
        );

        var list = _ctx.RenderComponent<StateMachinesPage>();
        list.WaitForAssertion(() => list.Markup.Should().Contain(draft.Id.ToString()), WaitTimeout);
        var detail = RenderDetail(draft.Machine, "user", draft.Id, draft.RowId);
        detail.WaitForAssertion(
            () => detail.Markup.Should().Contain("AwaitingPayment"),
            WaitTimeout
        );

        foreach (var markup in new[] { list.Markup, detail.Markup })
            markup
                .Should()
                .NotContain("cardNumber")
                .And.NotContain("4111", "an operator never sees what an instance holds")
                .And.NotContain(UserKey, "no operator view names the user behind a draft");
    }

    [Test]
    public async Task The_detail_page_names_the_owner_kind_so_a_users_draft_never_shows_as_a_system_instance()
    {
        var id = Guid.NewGuid();
        await SeedAsync(SnapshotOwnerKind.User, "Draft", DateTimeOffset.UtcNow, id);

        var asSystem = RenderDetail(Machine, "system", id, rowId: null);

        asSystem.WaitForAssertion(
            () => asSystem.Markup.Should().Contain($"No system instance of {Machine}"),
            WaitTimeout
        );
    }

    [Test]
    public async Task A_users_draft_without_its_row_is_refused_rather_than_guessed()
    {
        var draft = await SeedAsync(SnapshotOwnerKind.User, "Draft", DateTimeOffset.UtcNow);

        var page = RenderDetail(draft.Machine, "user", draft.Id, rowId: null);

        page.WaitForAssertion(
            () =>
            {
                page.Find(".cs-machine-refusal").TextContent.Should().Contain("row");
                page.Markup.Should().NotContain("Draft</span>");
            },
            WaitTimeout
        );
    }

    [Test]
    public void A_row_links_to_its_own_page_naming_its_owner_kind()
    {
        var id = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

        MachineInstanceRoutes
            .Detail("Acme/Odd Name", SnapshotOwnerKind.User, id, 42)
            .Should()
            .Be($"trax/data/state-machines/Acme%2FOdd%20Name/user/{id}?row=42");
        MachineInstanceRoutes
            .Detail(Machine, SnapshotOwnerKind.System, id, 7)
            .Should()
            .Be($"trax/data/state-machines/{Machine}/system/{id}");
    }

    [Test]
    public async Task The_instance_page_lists_the_runs_the_api_lists_each_linked_to_its_page()
    {
        var token = Guid.NewGuid().ToString("N");
        var instance = await SeedAsync(
            SnapshotOwnerKind.System,
            "Running",
            DateTimeOffset.UtcNow,
            invokeToken: token
        );
        var older = await SeedRunAsync(instance.Id, Guid.NewGuid().ToString("N"));
        var live = await SeedRunAsync(instance.Id, token);

        var page = RenderDetail(Machine, "system", instance.Id, rowId: null);

        using var scope = _ctx.Services.CreateScope();
        var api = (
            await new OperationsQueries().GetMachineInstance(
                Machine,
                SnapshotOwnerKind.System,
                instance.Id,
                scope.ServiceProvider.GetRequiredService<IOperationsService>(),
                default
            )
        )!;
        api.InvokedRuns.Select(r => r.Id).Should().Equal(live, older);

        page.WaitForAssertion(
            () =>
            {
                var links = page.FindAll(".cs-machine-runs a")
                    .Select(a => a.GetAttribute("href"))
                    .ToList();
                links
                    .Should()
                    .Equal(
                        api.InvokedRuns.Select(r => $"trax/data/metadata/{r.Id}"),
                        "the page lists the API's runs in its order, each linked to its run"
                    );
                page.FindAll(".cs-machine-live-run").Should().ContainSingle();
            },
            WaitTimeout
        );
    }

    [Test]
    public async Task Cancel_asks_for_confirmation_then_calls_the_shared_service()
    {
        var token = await SeedQueuedRunAsync();
        var instance = await SeedAsync(
            SnapshotOwnerKind.System,
            "Running",
            DateTimeOffset.UtcNow,
            invokeToken: token
        );
        var page = RenderDetail(Machine, "system", instance.Id, rowId: null);
        page.WaitForAssertion(() => page.Find(".cs-machine-cancel"), WaitTimeout);

        page.Find(".cs-machine-cancel").Click();
        (await EntryStatusAsync(token))
            .Should()
            .Be(WorkQueueStatus.Queued, "nothing is cancelled before the operator confirms");
        page.Find(".cs-machine-cancel-confirm-button").Click();

        page.WaitForAssertion(
            () => page.FindAll(".cs-machine-cancel-confirm").Should().BeEmpty(),
            WaitTimeout
        );
        (await EntryStatusAsync(token))
            .Should()
            .Be(
                WorkQueueStatus.Cancelled,
                "the button calls CancelMachineInstanceAsync, as cancelMachineInstance does"
            );
        page.FindAll(".cs-machine-action-error").Should().BeEmpty();
    }

    [Test]
    public async Task No_cancel_is_offered_on_a_users_draft_or_an_instance_with_no_live_run_and_a_refusal_shows_the_services_words()
    {
        var draft = await SeedAsync(
            SnapshotOwnerKind.User,
            "Running",
            DateTimeOffset.UtcNow,
            invokeToken: Guid.NewGuid().ToString("N")
        );
        var idle = await SeedAsync(SnapshotOwnerKind.System, "Done", DateTimeOffset.UtcNow);

        var userPage = RenderDetail(Machine, "user", draft.Id, draft.RowId);
        userPage.WaitForAssertion(() => userPage.Markup.Should().Contain("Running"), WaitTimeout);
        userPage
            .FindAll(".cs-machine-cancel")
            .Should()
            .BeEmpty("operators see a user's draft read-only");

        // Were the page to send one anyway, the service's refusal is shown word for word.
        await userPage.InvokeAsync(() => userPage.Instance.CancelInstance());
        userPage.WaitForAssertion(
            () =>
                userPage
                    .Find(".cs-machine-action-error")
                    .TextContent.Should()
                    .Contain(OperationsService.UserOwnedCancelRefusal),
            WaitTimeout
        );

        var idlePage = RenderDetail(Machine, "system", idle.Id, rowId: null);
        idlePage.WaitForAssertion(() => idlePage.Markup.Should().Contain("Done"), WaitTimeout);
        idlePage.FindAll(".cs-machine-cancel").Should().BeEmpty("there is no run to cancel");
    }

    private async Task<long> SeedRunAsync(Guid instance, string externalId)
    {
        await using var db = await _data.CreateDbContextAsync(default);
        var run = Metadata.Create(
            new CreateMetadata
            {
                Name = "Acme.IShipTrain",
                ExternalId = externalId,
                Input = null,
                InvokedBy = new InvokedBy(Machine, instance, SnapshotOwnerKind.System),
            }
        );
        run.TrainState = TrainState.InProgress;
        await db.Track(run);
        await db.SaveChanges(default);
        return run.Id;
    }

    private async Task<string> SeedQueuedRunAsync()
    {
        await using var db = await _data.CreateDbContextAsync(default);
        var entry = WorkQueue.Create(new CreateWorkQueue { TrainName = "Acme.IShipTrain" });
        await db.Track(entry);
        await db.SaveChanges(default);
        return entry.ExternalId;
    }

    private async Task<WorkQueueStatus> EntryStatusAsync(string externalId)
    {
        await using var db = await _data.CreateDbContextAsync(default);
        return db.WorkQueues.AsNoTracking().Single(w => w.ExternalId == externalId).Status;
    }

    private IRenderedComponent<StateMachineInstancePage> RenderDetail(
        string machine,
        string owner,
        Guid id,
        long? rowId
    )
    {
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        var path = $"trax/data/state-machines/{Uri.EscapeDataString(machine)}/{owner}/{id}";
        navigation.NavigateTo(rowId is null ? path : $"{path}?row={rowId}");
        return _ctx.RenderComponent<StateMachineInstancePage>(p =>
            p.Add(x => x.Machine, machine).Add(x => x.Owner, owner).Add(x => x.InstanceId, id)
        );
    }

    private async Task<SnapshotDraft> SeedAsync(
        SnapshotOwnerKind owner,
        string state,
        DateTimeOffset updatedAt,
        Guid? id = null,
        string? invokeToken = null
    )
    {
        await using var db = await _data.CreateDbContextAsync(default);
        var row = new SnapshotDraft
        {
            Id = id ?? Guid.NewGuid(),
            OwnerKind = owner,
            UserKey = owner == SnapshotOwnerKind.User ? UserKey : null,
            Machine = Machine,
            Version = 1,
            State = state,
            Context = SecretContext,
            ConcurrencyToken = Guid.NewGuid(),
            CreatedAt = updatedAt.AddMinutes(-1),
            UpdatedAt = updatedAt,
            InvokeToken = invokeToken,
        };
        db.SnapshotDrafts.Add(row);
        await db.SaveChanges(default);
        return row;
    }
}
