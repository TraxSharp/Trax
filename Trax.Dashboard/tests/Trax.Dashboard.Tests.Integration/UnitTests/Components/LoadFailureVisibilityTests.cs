using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Radzen.Blazor;
using Trax.Dashboard.Components.Layout.Header;
using Trax.Dashboard.Components.Shared;
using Trax.Dashboard.Models;
using Trax.Dashboard.Services.DashboardSettings;
using Trax.Dashboard.Services.LocalStorage;
using Trax.Dashboard.Tests.Integration.Fakes.Services;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// A failed load is shown, not swallowed. A server-mode grid whose load throws clears its rows
/// and says why, rather than leaving the previous filter's or page's rows under the new one;
/// a polling page whose refresh throws reports it, and the header says the data on screen is
/// from the last refresh that worked.
/// </summary>
[TestFixture]
public class LoadFailureVisibilityTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private Bunit.TestContext _ctx = null!;

    [SetUp]
    public void SetUp()
    {
        _ctx = new Bunit.TestContext();
        _ctx.Services.AddRadzenComponents();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    [Test]
    public async Task A_grid_load_that_throws_shows_an_error_and_no_rows()
    {
        var fail = false;
        var grid = _ctx.RenderComponent<TraxDataGrid<Row>>(p =>
            p.Add(
                    x => x.ServerLoadData,
                    (_, _) =>
                        fail
                            ? throw new InvalidOperationException("the database is unreachable")
                            : Task.FromResult(
                                new ServerDataResult<Row>([new Row("previous-row")], 1)
                            )
                )
                .Add(
                    x => x.Columns,
                    (RenderFragment)(
                        b =>
                        {
                            b.OpenComponent<RadzenDataGridColumn<Row>>(0);
                            b.AddAttribute(1, "Property", nameof(Row.Name));
                            b.CloseComponent();
                        }
                    )
                )
        );
        grid.WaitForAssertion(() => grid.Markup.Should().Contain("previous-row"), Wait);

        fail = true;
        await grid.InvokeAsync(() => grid.Instance.ReloadAsync());

        grid.WaitForAssertion(
            () => grid.Markup.Should().Contain("the database is unreachable"),
            Wait
        );
        grid.Markup.Should()
            .NotContain("previous-row", "rows from an earlier load do not stay under a failed one");
    }

    [Test]
    public async Task A_polling_refresh_that_throws_is_reported_to_the_dashboard_settings()
    {
        var settings = new FastPollingSettings();
        _ctx.Services.AddSingleton<IDashboardSettingsService>(settings);

        _ctx.RenderComponent<FailsAfterFirstLoad>();

        // A failed poll renders nothing on the page itself, so wait on the report, not a render.
        var reported = await settings.Failed.Task.WaitAsync(Wait);
        reported.Should().Be("the refresh failed");
    }

    [Test]
    public void The_header_says_when_the_last_refresh_failed()
    {
        IDashboardSettingsService settings = new DashboardSettingsService(
            new InMemoryLocalStorageService()
        );
        _ctx.Services.AddSingleton(settings);
        settings.NotifyPollFailed("the database is unreachable");

        var header = _ctx.RenderComponent<DashboardHeader>();

        header.Markup.Should().Contain("Last refresh failed");

        settings.NotifyPolled();
        header.Render();
        header.Markup.Should().NotContain("Last refresh failed", "a refresh that works clears it");
    }

    public sealed record Row(string Name);

    public sealed class FailsAfterFirstLoad : PollingComponentBase
    {
        private int _loads;

        protected override Task LoadDataAsync(CancellationToken cancellationToken) =>
            ++_loads == 1
                ? Task.CompletedTask
                : throw new InvalidOperationException("the refresh failed");
    }

    private sealed class FastPollingSettings : IDashboardSettingsService
    {
        public TimeSpan PollingInterval => TimeSpan.FromMilliseconds(10);
        public DateTime LastPollTime { get; private set; } = DateTime.UtcNow;
        public string? LastPollError { get; private set; }
        public bool HideAdminTrains => true;
        public IReadOnlyList<string> AdminTrainNames => [];

        public Task InitializeAsync() => Task.CompletedTask;

        public Task SetPollingIntervalAsync(int seconds) => Task.CompletedTask;

        public Task SetHideAdminTrainsAsync(bool hide) => Task.CompletedTask;

        public void NotifyPolled()
        {
            LastPollTime = DateTime.UtcNow;
            LastPollError = null;
        }

        public TaskCompletionSource<string> Failed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void NotifyPollFailed(string message)
        {
            LastPollError = message;
            Failed.TrySetResult(message);
        }

        public bool ShowSummaryCards => true;
        public bool ShowExecutionsChart => true;
        public bool ShowFailures => true;
        public bool ShowAvgDuration => true;
        public bool ShowServerHealth => true;

        public Task SetComponentVisibilityAsync(string key, bool visible) => Task.CompletedTask;
    }
}
