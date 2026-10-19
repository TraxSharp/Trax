using Microsoft.Playwright;
using Trax.Samples.ApiDashboard.Browser.Fixtures;

// One page object per area of the console. Each names its route and the heading that says it drew, and locates
// rows and controls by role and accessible name. A page item that changes an area's words or controls changes its
// page object with them, and the tests read the new words.
namespace Trax.Samples.ApiDashboard.Browser.Pages;

/// <summary>An area of the console at one route, drawn once its heading shows.</summary>
public abstract class AreaPage(ConsolePage console, string route, string heading)
{
    public ConsolePage Console { get; } = console;

    public IPage Page => Console.Page;

    public string Route { get; } = route;

    public ILocator Heading => Console.Heading(heading);

    /// <summary>Loads the area, signs in as <paramref name="caller"/>, and waits for its heading.</summary>
    public async Task OpenAsync(Caller caller)
    {
        await Console.OpenAsync(Route, caller);
        await Assertions
            .Expect(Heading)
            .ToBeVisibleAsync(new() { Timeout = BrowserTimeouts.Render.Ms() });
    }

    /// <summary>The row of the item with this id, by the checkbox the console labels <c>Select #id</c>.</summary>
    public ILocator Row(long id) => Console.RowSelectable(id);

    /// <summary>What the page says when the operations gate refuses the caller.</summary>
    public ILocator Refusal =>
        Page.GetByText(
            new System.Text.RegularExpressions.Regex(
                "not authori[sz]ed",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            )
        ).First;

    public ILocator Button(ILocator within, string name) =>
        within.GetByRole(AriaRole.Button, new() { Name = name, Exact = true });

    /// <summary>The page's own content, under the shell's navigation and header.</summary>
    public ILocator Main => Page.GetByRole(AriaRole.Main);

    /// <summary>The value a detail page's field list gives for <paramref name="label"/>.</summary>
    public ILocator DetailField(string label) =>
        Main.Locator($"xpath=.//dt[normalize-space()='{label}']/following-sibling::dd").First;
}

/// <summary>
/// The tab strip every Runs list shows (All, Failed, Queued, Dead letters), the current view marked.
/// </summary>
public static class RunViews
{
    public static ILocator Strip(ConsolePage console) =>
        console.Page.GetByRole(AriaRole.Navigation, new() { Name = "Run views", Exact = true });

    public static ILocator Tab(ConsolePage console, string name) =>
        Strip(console).GetByRole(AriaRole.Link, new() { Name = name, Exact = true });
}

/// <summary>The Overview: what needs attention first, each entry a link to its list, then the charts.</summary>
public sealed class OverviewPage(ConsolePage console) : AreaPage(console, "", "Overview")
{
    public ILocator Attention =>
        Page.GetByRole(AriaRole.Region, new() { Name = "Needs attention", Exact = true });

    /// <summary>The attention entry whose text matches <paramref name="text"/>.</summary>
    public ILocator AttentionLink(System.Text.RegularExpressions.Regex text) =>
        Attention.GetByRole(AriaRole.Link, new() { NameRegex = text });

    /// <summary>A bar of a chart, by the name it carries as a link.</summary>
    public ILocator ChartBar(string name) =>
        Page.GetByRole(AriaRole.Link, new() { Name = name, Exact = true });
}

/// <summary>Runs: every run the host has recorded.</summary>
public sealed class RunsPage(ConsolePage console) : AreaPage(console, "runs", "Runs")
{
    /// <summary>The Failed view: All runs filtered to state FAILED.</summary>
    public ILocator FailedHeading => Console.Heading("Failed runs");

    public ILocator ExternalIdFilter => Page.GetByLabel("External id", new() { Exact = true });

    /// <summary>
    /// The row of one run, found by filtering the list to its external id; a run that has ended has no checkbox
    /// to find it by. <paramref name="train"/> is the name its row links, the train's short name.
    /// </summary>
    public async Task<ILocator> FindAsync(string externalId, string train)
    {
        await ExternalIdFilter.FillAsync(externalId);
        var row = Console.RowLinked(train);
        // The list reloads once the filter settles; then only this run's row is left.
        await Assertions
            .Expect(
                Page.GetByRole(AriaRole.Row).Filter(new() { Has = Page.GetByRole(AriaRole.Link) })
            )
            .ToHaveCountAsync(1, new() { Timeout = BrowserTimeouts.Render.Ms() });
        return row;
    }
}

/// <summary>The runs list filtered through its URL, as a link to a filtered view opens it.</summary>
public sealed class FilteredRunsPage(ConsolePage console, string query)
    : AreaPage(console, $"runs?{query}", "Runs")
{
    /// <summary>The batch bar's button that cancels every run the filter matches.</summary>
    public ILocator CancelAllMatching(int count) =>
        Page.GetByRole(
            AriaRole.Button,
            new() { Name = $"Cancel all {count} matching", Exact = true }
        );
}

/// <summary>The bulk operations this host ran, under System.</summary>
public sealed class BulkOperationsPage(ConsolePage console)
    : AreaPage(console, "system/operations", "Bulk operations")
{
    public ILocator Operation(string title) => Console.RowLinked(title);
}

/// <summary>One bulk operation: its status, its counts, why ids did not take, and the filter it ran over.</summary>
public sealed class BulkOperationPage(ConsolePage console)
    : AreaPage(console, "system/operations", "Bulk operation")
{
    public ILocator Counts => Page.GetByRole(AriaRole.Region, new() { Name = "Counts" });

    /// <summary>The count labelled <paramref name="label"/> (Matched, Done, Failed, Not attempted).</summary>
    public ILocator Count(string label) =>
        Counts.GetByRole(AriaRole.Group, new() { Name = label, Exact = true });

    public ILocator Filter => Page.GetByRole(AriaRole.Region, new() { Name = "Filter" });
}

/// <summary>The work queue: queued, staged and dispatched entries.</summary>
public sealed class WorkQueuePage(ConsolePage console) : AreaPage(console, "runs/queued", "Queued")
{
    /// <summary>The link a row gives to an entry, a run or a manifest by its id: <c>#id</c>.</summary>
    public ILocator IdLink(ILocator row, long id) =>
        row.GetByRole(AriaRole.Link, new() { Name = $"#{id}", Exact = true });
}

/// <summary>One queue entry's page, by its id; the heading is its train's short name.</summary>
public sealed class QueueEntryPage(ConsolePage console, long id, string train)
    : AreaPage(console, $"runs/queued/{id}", train);

/// <summary>One queue entry's page: its fields, and what a queued entry waits on.</summary>
public sealed class WorkQueueEntryPage(ConsolePage console)
{
    public ILocator WaitingOn(long entry) =>
        console.Page.GetByRole(AriaRole.Link, new() { Name = $"Entry {entry}", Exact = true });
}

/// <summary>Dead letters, with their requeue and acknowledge actions.</summary>
public sealed class DeadLettersPage(ConsolePage console)
    : AreaPage(console, "runs/dead-letters", "Dead letters")
{
    public ILocator StatusFilter => Page.GetByRole(AriaRole.Combobox);

    /// <summary>Shows only the dead letters awaiting an operator.</summary>
    public async Task ShowAwaitingAsync() =>
        await StatusFilter.SelectOptionAsync(
            new SelectOptionValue { Label = "Awaiting intervention" }
        );

    /// <summary>Shows every dead letter as its own row instead of grouped by train and reason.</summary>
    public async Task ShowAsListAsync() =>
        await Page.GetByRole(AriaRole.Button, new() { Name = "List", Exact = true }).ClickAsync();

    /// <summary>
    /// The header row of the group of dead letters of <paramref name="train"/> (its short name) that were
    /// dead-lettered for <paramref name="reason"/>, by the checkbox that selects the group.
    /// </summary>
    public ILocator Group(string train, string reason) =>
        Page.GetByRole(AriaRole.Row)
            .Filter(
                new()
                {
                    Has = Page.GetByRole(
                        AriaRole.Checkbox,
                        new() { Name = $"Select group: {train}, {reason}", Exact = true }
                    ),
                }
            );

    /// <summary>Opens a group to its rows.</summary>
    public async Task ExpandAsync(string train, string reason) =>
        await Page.GetByRole(
                AriaRole.Button,
                new() { Name = $"Show {train}, {reason}", Exact = true }
            )
            .ClickAsync();

    /// <summary>Requeues one dead letter from its row, confirming in the console's dialog.</summary>
    public async Task RequeueAsync(long id)
    {
        await Button(Row(id), "Requeue").ClickAsync();
        await Console.ConfirmAsync("Requeue dead letter", $"Requeue dead letter #{id}?");
    }
}

/// <summary>One dead letter: why it was dead-lettered, its manifest, and Requeue and Acknowledge.</summary>
public sealed class DeadLetterPage(ConsolePage console, long id, string train = "IImportFeedTrain")
    : AreaPage(console, $"runs/dead-letters/{id}", train)
{
    public ILocator Details => Main;
}

/// <summary>Manifests: what runs on a schedule, in words, when it runs next, and whether it is enabled.</summary>
public sealed class ManifestsPage(ConsolePage console) : AreaPage(console, "schedules", "Manifests")
{
    /// <summary>The tab of Schedules named <paramref name="name"/>.</summary>
    public ILocator Tab(string name) =>
        Page.GetByRole(AriaRole.Tablist, new() { Name = "Schedules" })
            .GetByRole(AriaRole.Tab, new() { Name = name, Exact = true });
}

/// <summary>One manifest: its schedule, its next runs, its run history and its on/off switch.</summary>
public sealed class ManifestDetailPage(ConsolePage console, long id, string heading)
    : AreaPage(console, $"schedules/{id}", heading)
{
    public ILocator Schedule => Page.GetByRole(AriaRole.Region, new() { Name = "Schedule" });

    public ILocator RunHistory => Page.GetByRole(AriaRole.Region, new() { Name = "Run history" });

    public ILocator EnabledSwitch =>
        Page.GetByRole(AriaRole.Switch, new() { Name = "Manifest enabled", Exact = true });

    /// <summary>Switches the manifest off, confirming in the console's dialog.</summary>
    public async Task DisableAsync()
    {
        await EnabledSwitch.ClickAsync();
        await Console.ConfirmAsync("Disable manifest", "does not run on its schedule");
    }
}

/// <summary>Manifest groups, and the graph of which groups run after which.</summary>
public sealed class ManifestGroupsPage(ConsolePage console)
    : AreaPage(console, "schedules/groups", "Manifest groups")
{
    public ILocator Graph => Page.GetByRole(AriaRole.Group, new() { Name = "Dependency graph" });

    public ILocator NameFilter =>
        Page.GetByRole(AriaRole.Textbox, new() { Name = "Filter by name…", Exact = true });

    /// <summary>The node of the group named <paramref name="group"/>, which opens the group.</summary>
    public ILocator Node(string group) =>
        Graph.GetByRole(AriaRole.Button).Filter(new() { HasText = group });
}

/// <summary>One manifest group: its stats, its settings, its manifests and runs, and Queue and Cancel.</summary>
public sealed class ManifestGroupPage(ConsolePage console, long id, string name)
    : AreaPage(console, $"schedules/groups/{id}", name)
{
    public ILocator Setting(string label) => Main.GetByLabel(label, new() { Exact = true });
}

/// <summary>Machines: every machine the host registers, with its instance and stuck counts.</summary>
public sealed class MachinesPage(ConsolePage console) : AreaPage(console, "machines", "Machines")
{
    public ILocator List => Page.GetByRole(AriaRole.Region, new() { Name = "Registered machines" });

    /// <summary>The row of one machine, by the link to its page.</summary>
    public ILocator Machine(string name) =>
        List.GetByRole(AriaRole.Row)
            .Filter(
                new() { Has = Page.GetByRole(AriaRole.Link, new() { Name = name, Exact = true }) }
            );
}

/// <summary>One machine: its diagram, each state badged with its counts, and its instances under it.</summary>
public sealed class MachinePage : AreaPage
{
    private readonly string machine;

    public MachinePage(ConsolePage console, string machine)
        : base(console, $"machines/{Uri.EscapeDataString(machine)}", machine) =>
        this.machine = machine;

    public ILocator Diagram =>
        Page.GetByRole(AriaRole.Group, new() { Name = $"States of {machine}", Exact = true });

    /// <summary>A state's node: a button named by the state, then its badges.</summary>
    public ILocator State(string state) =>
        Diagram.GetByRole(
            AriaRole.Button,
            new()
            {
                NameRegex = new System.Text.RegularExpressions.Regex(
                    $"^{System.Text.RegularExpressions.Regex.Escape(state)}\\b"
                ),
            }
        );

    /// <summary>A badge on a state's node: <c>Instances</c> or <c>Stuck</c>.</summary>
    public ILocator Badge(string state, string badge) =>
        State(state)
            .GetByLabel(
                new System.Text.RegularExpressions.Regex(
                    $"^{System.Text.RegularExpressions.Regex.Escape(badge)}: \\d+$"
                )
            );

    public ILocator Instances => Page.GetByRole(AriaRole.Region, new() { Name = "Instances" });

    public ILocator StuckOnly => Page.GetByRole(AriaRole.Checkbox, new() { Name = "Stuck only" });

    /// <summary>The row of one instance, by the link its id is.</summary>
    public ILocator Instance(Guid id) =>
        Instances
            .GetByRole(AriaRole.Row)
            .Filter(
                new()
                {
                    Has = Page.GetByRole(AriaRole.Link, new() { Name = $"{id}", Exact = true }),
                }
            );

    /// <summary>What the page says when the machine's rules cannot be drawn.</summary>
    public ILocator NoDiagram => Page.GetByText($"{machine} has no diagram");
}

/// <summary>
/// One machine instance: the machine drawn with its state lit, what it accepts and waits on, and its runs.
/// </summary>
public sealed class MachineInstancePage : AreaPage
{
    private readonly string machine;

    public MachineInstancePage(ConsolePage console, string machine, Guid id)
        : base(console, $"machines/{Uri.EscapeDataString(machine)}/system/{id}", machine) =>
        this.machine = machine;

    public ILocator Diagram =>
        Page.GetByRole(AriaRole.Group, new() { Name = $"States of {machine}", Exact = true });

    /// <summary>The state the instance is in, as the diagram lights it.</summary>
    public ILocator Lit => Diagram.Locator("[aria-current='step']");

    public ILocator WhereItIs => Page.GetByRole(AriaRole.Region, new() { Name = "Where it is" });

    /// <summary>The value the "Where it is" panel gives for <paramref name="label"/>.</summary>
    public ILocator Field(string label) =>
        WhereItIs.Locator($"xpath=.//dt[normalize-space()='{label}']/following-sibling::dd");

    public ILocator Commands =>
        Page.GetByRole(AriaRole.List, new() { Name = "Commands it accepts" });
}

/// <summary>Persisted operations, served only to a caller who may manage them.</summary>
public sealed class PersistedOperationsPage(ConsolePage console)
    : AreaPage(console, "api-admin/persisted-operations", "Persisted operations")
{
    public ILocator Operation(string id) => Console.RowLinked(id);

    /// <summary>What a caller outside the management gate is told instead of the list.</summary>
    public ILocator NotServed => Page.GetByText("your account may not manage them");
}

/// <summary>One persisted operation, by its id, in the default tenant: its fields, its document, its history.</summary>
public sealed class PersistedOperationPage(ConsolePage console, string id)
    : AreaPage(console, $"api-admin/persisted-operations/{Uri.EscapeDataString(id)}", id);

/// <summary>
/// The scheduler's settings: the manifest manager and the job dispatcher paused and resumed one at a time, then
/// a section per concern, each with its own Save.
/// </summary>
public sealed class SchedulerPage(ConsolePage console)
    : AreaPage(console, "system/scheduler", "Scheduler")
{
    /// <summary>The row of one of the scheduler's loops, by its name: "Job dispatcher", "Manifest manager".</summary>
    public ILocator Loop(string name) =>
        Page.GetByRole(AriaRole.Listitem, new() { Name = name, Exact = true });

    /// <summary>A settings section, by its heading: "Retry", "Job timeouts".</summary>
    public ILocator Section(string name) =>
        Page.GetByRole(AriaRole.Region, new() { Name = name, Exact = true });

    /// <summary>Pauses or resumes a loop from its row, confirming in the console's dialog.</summary>
    public async Task SwitchAsync(string verb, string loop, string contains)
    {
        var action = $"{verb} {loop.ToLowerInvariant()}";
        await Button(Loop(loop), action).ClickAsync();
        await Console.ConfirmAsync(action, contains);
    }
}

/// <summary>Hosts: every process that beats, what it runs, and the dashboard process's own stats.</summary>
public sealed class HostsPage(ConsolePage console) : AreaPage(console, "system/hosts", "Hosts")
{
    public ILocator Hosts => Page.GetByRole(AriaRole.Table, new() { Name = "Hosts", Exact = true });

    public ILocator ThisProcess =>
        Page.GetByRole(AriaRole.Region, new() { Name = "This dashboard's process", Exact = true });
}

/// <summary>Every run's log entries, with filters held in the URL.</summary>
public sealed class LogsPage(ConsolePage console) : AreaPage(console, "system/logs", "Logs")
{
    public ILocator MinimumLevel => Page.GetByLabel("Minimum level", new() { Exact = true });
}

/// <summary>What this browser keeps for itself.</summary>
public sealed class PreferencesPage(ConsolePage console)
    : AreaPage(console, "preferences", "Preferences")
{
    public ILocator HideSchedulerTrains =>
        Page.GetByRole(AriaRole.Checkbox, new() { Name = "Hide scheduler trains", Exact = true });

    public ILocator RefreshEvery =>
        Page.GetByLabel("Refresh this page every (seconds)", new() { Exact = true });
}

/// <summary>
/// One run: its header and actions, its chain on the graph with the selected step's panel, and the tabs under it.
/// The heading is the train's short name.
/// </summary>
public sealed class RunPage(ConsolePage console, long id, string train)
    : AreaPage(console, $"runs/{id}", train)
{
    public ILocator Graph => Page.GetByRole(AriaRole.Region, new() { Name = "Run graph" });

    /// <summary>A step's node on the graph, by the name it is drawn with.</summary>
    public ILocator Node(string step) =>
        Graph
            .GetByRole(AriaRole.Group, new() { Name = "Steps of the train" })
            .GetByRole(
                AriaRole.Button,
                new()
                {
                    NameRegex = new System.Text.RegularExpressions.Regex(
                        "^" + System.Text.RegularExpressions.Regex.Escape(step) + " "
                    ),
                }
            );

    /// <summary>The panel of the step selected on the graph.</summary>
    public ILocator StepPanel(string step) =>
        Graph.GetByRole(AriaRole.Complementary, new() { Name = $"Step {step}", Exact = true });

    public ILocator Timeline =>
        Page.GetByRole(AriaRole.Region, new() { Name = "Junction timeline" });

    /// <summary>A step of the timeline that selects its node, by the junction's name.</summary>
    public ILocator TimelineStep(string name) =>
        Timeline.GetByRole(
            AriaRole.Button,
            new()
            {
                NameRegex = new System.Text.RegularExpressions.Regex(
                    @"^Step #\d+ " + System.Text.RegularExpressions.Regex.Escape(name) + "$"
                ),
            }
        );

    public ILocator Action(string name) =>
        Page.GetByRole(AriaRole.Button, new() { Name = name, Exact = true });

    public ILocator Link(string name) =>
        Page.GetByRole(AriaRole.Link, new() { Name = name, Exact = true });

    public ILocator Tab(string name) =>
        Page.GetByRole(AriaRole.Tab, new() { Name = name, Exact = true });
}

/// <summary>One train's page: its chain, its recent runs by step, what starts it, and Run and Queue.</summary>
public sealed class TrainPage(ConsolePage console, Type train)
    : AreaPage(console, $"trains/{train.FullName}", ShortName(train))
{
    public ILocator Chain =>
        Page.GetByRole(
            AriaRole.Group,
            new() { Name = $"Chain of {ShortName(train)}", Exact = true }
        );

    public ILocator Step(string name) => Chain.GetByText(name, new() { Exact = true });

    /// <summary>The badge on a step that counts the range's failed runs that stopped there.</summary>
    public ILocator FailureCount =>
        Chain.GetByLabel(
            new System.Text.RegularExpressions.Regex(@"^Failed runs in the last 24 hours: \d+$")
        );

    public ILocator Grid =>
        Page.GetByRole(AriaRole.Table, new() { Name = "Recent runs by step", Exact = true });

    /// <summary>The grid's cell for one step of one run, which links the run.</summary>
    public ILocator Cell(long run, string step, string state) =>
        Grid.GetByRole(
            AriaRole.Link,
            new() { Name = $"Run #{run}, {step}: {state}", Exact = true }
        );

    public ILocator NeverRan => Page.GetByText("This train has not run yet.");

    public ILocator Manifests => Page.GetByRole(AriaRole.Region, new() { Name = "Manifests" });

    public ILocator StartedByMachines =>
        Page.GetByRole(AriaRole.Region, new() { Name = "Started by machines" });

    private static string ShortName(Type train) => train.Name;
}
