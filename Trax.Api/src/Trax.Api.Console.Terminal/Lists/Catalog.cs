using Trax.Api.Console.Terminal.Operations;
using Trax.Client;

namespace Trax.Api.Console.Terminal.Lists;

/// <summary>
/// The lists the console shows, each read and acted on with the browser console's own documents: runs, dead letters,
/// the work queue and manifests. The terminal and the headless mode both read them from here.
/// </summary>
internal static class Catalog
{
    /// <summary>
    /// The scheduler's own trains (the manifest manager, the dispatcher, the runner), hidden unless asked for, as the
    /// browser console hides them. Declared before the lists, which read it as they are built.
    /// </summary>
    private static readonly ListFilter AdminTrains = new(
        "admin-trains",
        "The scheduler's own trains: hidden unless shown.",
        ["hide", "show"]
    );

    public static ConsoleList<ExecutionsResultOperationsExecutionsItems> Runs { get; } =
        new()
        {
            Name = "runs",
            Title = "Runs",
            Columns =
            [
                new("Id", r => r.Id.ToString()),
                new("Train", r => r.Name),
                new("State", r => Wire.Name(r.TrainState)),
                new("Started", r => Wire.Time(r.StartTime)),
                new("Ended", r => Wire.Time(r.EndTime)),
                new("Junction", r => r.CurrentlyRunningJunction ?? r.FailureJunction ?? ""),
                new("Reason", r => r.FailureReason ?? ""),
            ],
            IdOf = r => r.Id,
            RunOf = r => r.Id,
            CodeOf = r => r.Name,
            RefreshOn = ChangeDomain.Execution,
            Filters =
            [
                new("state", "The run's state.", Wire.Names<TrainState>()),
                new("train", "The train's name, exactly."),
                new("failure-class", "How a failure was classified.", Wire.Names<FailureClass>()),
                new("manifest", "The manifest that scheduled it."),
                new("parent", "The run that started it."),
                new("host", "The host that ran it."),
                new("external-id", "Its external id."),
                AdminTrains,
            ],
            Load = async (client, query, ct) =>
            {
                var f = query.Filters;
                var answer = await client.ExecutionsAsync(
                    new ExecutionsVariables
                    {
                        Take = query.Take,
                        AfterId = query.After,
                        TrainState = Enum<TrainState>(f, "state"),
                        TrainName = Text(f, "train"),
                        FailureClass = Enum<FailureClass>(f, "failure-class"),
                        ManifestId = Long(f, "manifest"),
                        ParentId = Long(f, "parent"),
                        HostName = Text(f, "host"),
                        ExternalId = Text(f, "external-id"),
                        HideAdminTrains = HideAdmin(f),
                    },
                    ct
                );
                return Page(
                    answer,
                    d =>
                        d.Operations?.Executions is { } p
                            ? ListPage.Of(p.Items, p.TotalCount, p.IsEstimatedCount, p.NextCursor)
                            : null
                );
            },
            Detail = async (client, id, ct) =>
                Detail(
                    await client.ExecutionDetailAsync(new ExecutionDetailVariables { Id = id }, ct),
                    d => d.Operations?.ExecutionDetail
                ),
            Actions =
            [
                new(
                    "cancel",
                    'c',
                    "Ask the selected runs to stop.",
                    NeedsNote: false,
                    Confirm: true,
                    async (client, ids, _, ct) =>
                        ActionOutcome.Of(
                            await client.CancelExecutionsAsync(
                                new CancelExecutionsVariables { Ids = ids },
                                ct
                            ),
                            d =>
                                Said(
                                    d.Operations?.CancelExecutions is { } r
                                        ? (r.Success, r.Message)
                                        : null
                                )
                        )
                ),
            ],
            BulkActions =
            [
                new(
                    "cancel",
                    BulkOperationKind.Cancel,
                    NeedsNote: false,
                    f => new BulkOperationFilterInput
                    {
                        Executions = new BulkExecutionFilterInput
                        {
                            TrainState = Enum<TrainState>(f, "state"),
                            TrainName = Text(f, "train"),
                            FailureClass = Enum<FailureClass>(f, "failure-class"),
                            ManifestId = Long(f, "manifest"),
                            ParentId = Long(f, "parent"),
                            HostName = Text(f, "host"),
                            ExternalId = Text(f, "external-id"),
                            HideAdminTrains = HideAdmin(f),
                        },
                    }
                ),
            ],
        };

    public static ConsoleList<DeadLettersResultOperationsDeadLettersDeadLettersItems> DeadLetters { get; } =
        new()
        {
            Name = "dead-letters",
            Title = "Dead letters",
            Columns =
            [
                new("Id", r => r.Id.ToString()),
                new("Manifest", r => r.ManifestName),
                new("Status", r => Wire.Name(r.Status)),
                new("At", r => Wire.Time(r.DeadLetteredAt)),
                new("Retries", r => r.RetryCountAtDeadLetter.ToString()),
                new("Reason", r => r.Reason),
            ],
            IdOf = r => r.Id,
            RefreshOn = ChangeDomain.DeadLetter,
            Filters =
            [
                new("status", "Where it stands.", Wire.Names<DeadLetterStatus>()),
                new("manifest", "The manifest whose run it is."),
            ],
            Load = async (client, query, ct) =>
            {
                var f = query.Filters;
                var answer = await client.DeadLettersAsync(
                    new DeadLettersVariables
                    {
                        Take = query.Take,
                        AfterId = query.After,
                        Status = Enum<DeadLetterStatus>(f, "status"),
                        ManifestId = Long(f, "manifest"),
                    },
                    ct
                );
                return Page(
                    answer,
                    d =>
                        d.Operations?.DeadLetters.DeadLetters is { } p
                            ? ListPage.Of(p.Items, p.TotalCount, p.IsEstimatedCount, p.NextCursor)
                            : null
                );
            },
            Detail = async (client, id, ct) =>
                Detail(
                    await client.DeadLetterDetailAsync(
                        new DeadLetterDetailVariables { Id = id },
                        ct
                    ),
                    d => d.Operations?.DeadLetters.DeadLetter
                ),
            Actions =
            [
                new(
                    "requeue",
                    'r',
                    "Queue the selected dead letters' runs again.",
                    NeedsNote: false,
                    Confirm: true,
                    async (client, ids, options, ct) =>
                        ActionOutcome.Of(
                            await client.RequeueDeadLettersAsync(
                                new RequeueDeadLettersVariables
                                {
                                    Ids = ids,
                                    AskAfresh = options.AskAfresh,
                                },
                                ct
                            ),
                            d =>
                                Said(
                                    d.Operations?.DeadLetters.RequeueDeadLetters is { } r
                                        ? (r.Count > 0, r.Message)
                                        : null
                                )
                        ),
                    AsksAfresh: true
                ),
                new(
                    "acknowledge",
                    'a',
                    "Mark the selected dead letters handled, with a note.",
                    NeedsNote: true,
                    Confirm: false,
                    async (client, ids, options, ct) =>
                        ActionOutcome.Of(
                            await client.AcknowledgeDeadLettersAsync(
                                new AcknowledgeDeadLettersVariables
                                {
                                    Ids = ids,
                                    Note = options.Note ?? "",
                                },
                                ct
                            ),
                            d =>
                                Said(
                                    d.Operations?.DeadLetters.AcknowledgeDeadLetters is { } r
                                        ? (r.Count > 0, r.Message)
                                        : null
                                )
                        )
                ),
            ],
            BulkActions =
            [
                new(
                    "requeue",
                    BulkOperationKind.Requeue,
                    NeedsNote: false,
                    DeadLetterFilter,
                    AsksAfresh: true
                ),
                new(
                    "acknowledge",
                    BulkOperationKind.Acknowledge,
                    NeedsNote: true,
                    DeadLetterFilter
                ),
            ],
        };

    public static ConsoleList<WorkQueueResultOperationsWorkQueueWorkQueuesItems> WorkQueue { get; } =
        new()
        {
            Name = "work-queue",
            Title = "Work queue",
            Columns =
            [
                new("Id", r => r.Id.ToString()),
                new("Train", r => r.TrainName),
                new("Status", r => Wire.Name(r.Status)),
                new("Created", r => Wire.Time(r.CreatedAt)),
                new("Dispatched", r => Wire.Time(r.DispatchedAt)),
                new("Priority", r => r.Priority.ToString()),
                new("Subject", r => r.SubjectKey ?? ""),
            ],
            IdOf = r => r.Id,
            RunOf = r => r.MetadataId,
            CodeOf = r => r.TrainName,
            RefreshOn = ChangeDomain.WorkQueue,
            Filters =
            [
                new("status", "Where the entry stands.", Wire.Names<WorkQueueStatus>()),
                new("train", "The train's name, exactly."),
                new("subject", "The subject key it is queued under."),
                new("manifest", "The manifest that queued it."),
            ],
            Load = async (client, query, ct) =>
            {
                var f = query.Filters;
                var answer = await client.WorkQueueAsync(
                    new WorkQueueVariables
                    {
                        Take = query.Take,
                        AfterId = query.After,
                        Status = Enum<WorkQueueStatus>(f, "status"),
                        TrainName = Text(f, "train"),
                        SubjectKey = Text(f, "subject"),
                        ManifestId = Long(f, "manifest"),
                    },
                    ct
                );
                return Page(
                    answer,
                    d =>
                        d.Operations?.WorkQueue.WorkQueues is { } p
                            ? ListPage.Of(p.Items, p.TotalCount, p.IsEstimatedCount, p.NextCursor)
                            : null
                );
            },
            Detail = async (client, id, ct) =>
                Detail(
                    await client.WorkQueueDetailAsync(new WorkQueueDetailVariables { Id = id }, ct),
                    d => d.Operations?.WorkQueue.Detail
                ),
            Actions =
            [
                new(
                    "cancel",
                    'c',
                    "Take the selected entries off the queue.",
                    NeedsNote: false,
                    Confirm: true,
                    async (client, ids, _, ct) =>
                        ActionOutcome.Of(
                            await client.CancelWorkQueueEntriesAsync(
                                new CancelWorkQueueEntriesVariables { Ids = ids },
                                ct
                            ),
                            d =>
                                Said(
                                    d.Operations?.WorkQueue.CancelWorkQueueEntries is { } r
                                        ? (r.Success, r.Message)
                                        : null
                                )
                        )
                ),
            ],
        };

    public static ConsoleList<ManifestsResultOperationsManifestsItems> Manifests { get; } =
        new()
        {
            Name = "manifests",
            Title = "Manifests",
            Columns =
            [
                new("Id", r => r.Id.ToString()),
                new("Name", r => r.Name),
                new("Enabled", r => r.IsEnabled ? "yes" : "no"),
                new(
                    "Schedule",
                    r =>
                        r.CronExpression
                        ?? (r.IntervalSeconds is { } s ? $"every {s}s" : Wire.Name(r.ScheduleType))
                ),
                new("Last success", r => Wire.Time(r.LastSuccessfulRun)),
                new("Retries", r => r.MaxRetries.ToString()),
            ],
            IdOf = r => r.Id,
            RefreshOn = ChangeDomain.Manifest,
            Filters =
            [
                new("enabled", "Whether it is enabled.", ["true", "false"]),
                new("schedule", "How it is scheduled.", Wire.Names<ScheduleType>()),
                new("name", "Part of its name."),
                new("group", "Its manifest group."),
                AdminTrains,
            ],
            Load = async (client, query, ct) =>
            {
                var f = query.Filters;
                var answer = await client.ManifestsAsync(
                    new ManifestsVariables
                    {
                        Take = query.Take,
                        AfterId = query.After,
                        IsEnabled = Bool(f, "enabled"),
                        ScheduleType = Enum<ScheduleType>(f, "schedule"),
                        NameContains = Text(f, "name"),
                        ManifestGroupId = Long(f, "group"),
                        HideAdminTrains = HideAdmin(f),
                    },
                    ct
                );
                return Page(
                    answer,
                    d =>
                        d.Operations?.Manifests is { } p
                            ? ListPage.Of(p.Items, p.TotalCount, p.IsEstimatedCount, p.NextCursor)
                            : null
                );
            },
            Detail = async (client, id, ct) =>
                Detail(
                    await client.ManifestDetailAsync(new ManifestDetailVariables { Id = id }, ct),
                    d => d.Operations?.ManifestDetail
                ),
            Actions =
            [
                new(
                    "trigger",
                    't',
                    "Queue a run of each selected manifest now.",
                    NeedsNote: false,
                    Confirm: true,
                    async (client, ids, options, ct) =>
                        ActionOutcome.Of(
                            await client.TriggerManifestsAsync(
                                new TriggerManifestsVariables
                                {
                                    Ids = ids,
                                    AskAfresh = options.AskAfresh,
                                },
                                ct
                            ),
                            d =>
                                Said(
                                    d.Operations?.TriggerManifests is { } r
                                        ? (r.Success, r.Message)
                                        : null
                                )
                        ),
                    AsksAfresh: true
                ),
                new(
                    "enable",
                    'e',
                    "Let the selected manifests schedule runs.",
                    NeedsNote: false,
                    Confirm: false,
                    (client, ids, _, ct) => SetEnabled(client, ids, true, ct)
                ),
                new(
                    "disable",
                    'd',
                    "Stop the selected manifests scheduling runs.",
                    NeedsNote: false,
                    Confirm: true,
                    (client, ids, _, ct) => SetEnabled(client, ids, false, ct)
                ),
            ],
            BulkActions =
            [
                new(
                    "trigger",
                    BulkOperationKind.Trigger,
                    NeedsNote: false,
                    ManifestFilter,
                    AsksAfresh: true
                ),
                new("enable", BulkOperationKind.Enable, NeedsNote: false, ManifestFilter),
                new("disable", BulkOperationKind.Disable, NeedsNote: false, ManifestFilter),
            ],
        };

    /// <summary>Every list, in the order the terminal shows them.</summary>
    public static IReadOnlyList<IConsoleList> All { get; } =
    [Runs, DeadLetters, WorkQueue, Manifests];

    /// <summary>The list named <paramref name="name"/>.</summary>
    /// <exception cref="InvalidOperationException">No list is named so.</exception>
    public static IConsoleList Named(string name) =>
        All.FirstOrDefault(l => l.Name == name)
        ?? throw new InvalidOperationException(
            $"'{name}' is not a list. Use one of {string.Join(", ", All.Select(l => l.Name))}."
        );

    /// <summary>
    /// Starts a bulk operation over every row a list's filter matches. <paramref name="expectedCount"/> is the count
    /// the operator confirmed; the host refuses when the filter now matches another (Trax.Api ADR 0045).
    /// </summary>
    public static async Task<(ActionOutcome Outcome, Guid? Operation)> StartBulkAsync(
        TraxClient client,
        BulkAction action,
        IReadOnlyDictionary<string, string> filters,
        int expectedCount,
        ActionOptions options,
        CancellationToken ct
    )
    {
        var answer = await client.StartBulkOperationAsync(
            new StartBulkOperationVariables
            {
                Kind = action.Kind,
                Filter = action.Filter(filters),
                ExpectedCount = expectedCount,
                AskAfresh = options.AskAfresh,
                Note = options.Note,
            },
            ct
        );
        var outcome = ActionOutcome.Of(
            answer,
            d => Said(d.Operations?.StartBulkOperation is { } r ? (r.Success, r.Message) : null)
        );
        return (outcome, answer.Data?.Operations?.StartBulkOperation.Operation?.Id);
    }

    private static BulkOperationFilterInput DeadLetterFilter(
        IReadOnlyDictionary<string, string> f
    ) =>
        new()
        {
            DeadLetters = new BulkDeadLetterFilterInput
            {
                Status = Enum<DeadLetterStatus>(f, "status"),
                ManifestId = Long(f, "manifest"),
            },
        };

    private static BulkOperationFilterInput ManifestFilter(IReadOnlyDictionary<string, string> f) =>
        new()
        {
            Manifests = new BulkManifestFilterInput
            {
                IsEnabled = Bool(f, "enabled"),
                ScheduleType = Enum<ScheduleType>(f, "schedule"),
                NameContains = Text(f, "name"),
                ManifestGroupId = Long(f, "group"),
                HideAdminTrains = HideAdmin(f),
            },
        };

    private static async Task<ActionOutcome> SetEnabled(
        TraxClient client,
        IReadOnlyList<long> ids,
        bool enabled,
        CancellationToken ct
    ) =>
        ActionOutcome.Of(
            await client.SetManifestsEnabledAsync(
                new SetManifestsEnabledVariables { Ids = ids, Enabled = enabled },
                ct
            ),
            d => Said(d.Operations?.SetManifestsEnabled is { } r ? (r.Success, r.Message) : null)
        );

    // An answer whose operations came back null: the gate let the request through but the field was refused.
    private static (bool, string) Said((bool Ok, string? Message)? result) =>
        result is var (ok, message)
            ? (ok, message ?? (ok ? "Done." : "The host did not do it."))
            : (
                false,
                "The host answered without the operations namespace: the credential may not reach it."
            );

    private static TraxAnswer<ListPage<TRow>> Page<TResult, TRow>(
        TraxAnswer<TResult> answer,
        Func<TResult, ListPage<TRow>?> read
    ) =>
        answer.Succeeded && read(answer.Data!) is { } page
            ? new TraxAnswer<ListPage<TRow>> { Data = page }
            : new TraxAnswer<ListPage<TRow>>
            {
                Errors = answer.Errors,
                Failure =
                    answer.Failure
                    ?? (
                        answer.Errors.Count == 0
                            ? new TraxTransportFailure(
                                "The host answered without the operations namespace: the credential may not reach it."
                            )
                            : null
                    ),
            };

    private static TraxAnswer<IReadOnlyList<(string Label, string Value)>> Detail<TResult, TDetail>(
        TraxAnswer<TResult> answer,
        Func<TResult, TDetail?> read
    )
        where TDetail : class =>
        answer.Succeeded && read(answer.Data!) is { } detail
            ? new() { Data = Wire.Fields(detail) }
            : new()
            {
                Errors = answer.Errors,
                Failure =
                    answer.Failure
                    ?? (answer.Errors.Count == 0 ? new TraxTransportFailure("Not found.") : null),
            };

    private static bool HideAdmin(IReadOnlyDictionary<string, string> f) =>
        !string.Equals(
            f.GetValueOrDefault("admin-trains"),
            "show",
            StringComparison.OrdinalIgnoreCase
        );

    private static Optional<string?> Text(IReadOnlyDictionary<string, string> f, string name) =>
        f.TryGetValue(name, out var value) && value.Length > 0 ? value : default(Optional<string?>);

    private static Optional<long?> Long(IReadOnlyDictionary<string, string> f, string name) =>
        f.TryGetValue(name, out var value) && value.Length > 0
            ? Wire.Long(name, value)
            : default(Optional<long?>);

    private static Optional<bool?> Bool(IReadOnlyDictionary<string, string> f, string name) =>
        f.TryGetValue(name, out var value) && value.Length > 0
            ? Wire.Bool(name, value)
            : default(Optional<bool?>);

    private static Optional<TEnum?> Enum<TEnum>(IReadOnlyDictionary<string, string> f, string name)
        where TEnum : struct, System.Enum =>
        f.TryGetValue(name, out var value) && value.Length > 0
            ? Wire.Parse<TEnum>(name, value)
            : default(Optional<TEnum?>);
}
