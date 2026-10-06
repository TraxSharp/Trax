using System.ComponentModel;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Radzen;
using Trax.Dashboard.Services.DashboardSettings;
using Trax.Scheduler.Services.Operations;

namespace Trax.Dashboard.Components.Shared;

/// <summary>
/// Base component that polls for data on a configurable interval.
/// Subclasses override <see cref="LoadDataAsync"/> with their query logic.
/// The first load shows <see cref="IsLoading"/> = true; subsequent refreshes are silent.
/// The polling interval is read from <see cref="IDashboardSettingsService"/> each cycle,
/// so changes from the settings page take effect on the next tick automatically.
///
/// Subclasses with route parameters should override <see cref="GetRouteKey"/> so that
/// navigating between instances of the same page (e.g. /metadata/1 → /metadata/2)
/// triggers an immediate reload instead of waiting for the next poll tick.
///
/// Same-URL navigation (e.g. clicking a sidebar link for the page you're already on,
/// or clicking the highlighted node in a DAG graph) is intercepted via
/// <see cref="NavigationManager.RegisterLocationChangingHandler"/> — the navigation is
/// prevented and <see cref="RefreshNowAsync"/> is called instead.
///
/// A load that throws, whether the first load, a route-change reload or a background tick, is
/// reported through <see cref="IDashboardSettingsService.NotifyPollFailed"/>, recorded in
/// <see cref="LoadError"/> and retried on the next tick. It never leaves a lifecycle method,
/// where Blazor Server would treat it as fatal and end the operator's circuit.
/// Infrastructure for the dashboard's own pages; it is public only because those pages derive
/// from it, and is not intended for use outside this package.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public abstract class PollingComponentBase : ComponentBase, IAsyncDisposable
{
    /// <summary>
    /// The circuit's dashboard preferences, injected. Supplies the polling interval and receives
    /// <see cref="IDashboardSettingsService.NotifyPolled"/> after every completed load.
    /// </summary>
    [Inject]
    private protected IDashboardSettingsService DashboardSettings { get; set; } = default!;

    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    private NotificationService BatchNotifications { get; set; } = default!;

    private CancellationTokenSource? _cts;
    private object? _lastRouteKey;
    private IDisposable? _locationChangingRegistration;

    /// <summary>
    /// <see langword="true"/> until the first load finishes, and again during a
    /// <see cref="RefreshNowAsync"/> call made with <c>showLoading: true</c>. Background poll
    /// ticks never set it.
    /// </summary>
    private protected bool IsLoading { get; set; } = true;

    /// <summary>
    /// The message of the most recent load that failed, or <see langword="null"/> once a load
    /// succeeds. A page whose data is still empty after a failed load renders this as "could not
    /// load" rather than "not found", because nothing is known about whether the row exists.
    /// </summary>
    private protected string? LoadError { get; private set; }

    /// <summary>
    /// When true, the polling loop skips data refreshes until the value is set back to false.
    /// Use this to prevent data reloads while the user has an active selection or is in the
    /// middle of an interaction (e.g. batch operations with checkboxes).
    /// The next poll tick after the flag is cleared will refresh normally.
    /// </summary>
    private protected bool PausePolling { get; set; }

    /// <summary>
    /// Error message from the most recent batch operation, displayed as an alert.
    /// Cleared at the start of each batch operation.
    /// </summary>
    private protected string? BatchError { get; set; }

    /// <summary>
    /// True while a batch operation is in progress. Used to disable action buttons.
    /// </summary>
    private protected bool BatchOperating { get; set; }

    /// <summary>
    /// Runs a batch operation with standardized error handling, loading state, and polling control.
    /// Clears <see cref="BatchError"/>, sets <see cref="BatchOperating"/> during execution,
    /// invokes <paramref name="onSuccess"/> on success, unpauses polling, and reloads data.
    /// </summary>
    /// <param name="operation">The async operation to execute.</param>
    /// <param name="onSuccess">Optional callback invoked after the operation succeeds (e.g. clear selection).</param>
    private protected async Task RunBatchOperationAsync(
        Func<Task> operation,
        Action? onSuccess = null
    )
    {
        BatchError = null;
        BatchOperating = true;

        try
        {
            await operation();
            onSuccess?.Invoke();
            PausePolling = false;
            await LoadDataAsync(DisposalToken);
        }
        catch (Exception ex)
        {
            BatchError = ex.Message;
        }
        finally
        {
            BatchOperating = false;
        }
    }

    /// <summary>
    /// Runs a batch operation answered by the operations service, and reports what the service
    /// said. A refusal (no ids, more than the service's batch limit) is shown as
    /// <see cref="BatchError"/> and leaves the selection and polling as they are, so the operator
    /// can change the selection and try again. An accepted batch is reported with the service's
    /// message, as a warning when it changed nothing; then <paramref name="onSuccess"/> runs,
    /// polling resumes and the data reloads. An exception is shown as <see cref="BatchError"/>.
    /// </summary>
    /// <param name="summary">The notification's title, such as "Entries Cancelled".</param>
    /// <param name="operation">The service call.</param>
    /// <param name="onSuccess">Optional callback invoked after the service accepted the batch.</param>
    private protected async Task RunBatchOperationAsync(
        string summary,
        Func<Task<OperationResult>> operation,
        Action? onSuccess = null
    )
    {
        BatchError = null;
        BatchOperating = true;

        try
        {
            var result = await operation();
            if (!result.Success)
            {
                BatchError = result.Message ?? $"{summary}: the request was refused.";
                return;
            }

            BatchNotifications.Notify(
                result.Count == 0 ? NotificationSeverity.Warning : NotificationSeverity.Success,
                summary,
                result.Message ?? "",
                duration: 4000
            );
            onSuccess?.Invoke();
            PausePolling = false;
            await LoadDataAsync(DisposalToken);
        }
        catch (Exception ex)
        {
            BatchError = ex.Message;
        }
        finally
        {
            BatchOperating = false;
        }
    }

    /// <summary>
    /// Runs a batch trigger answered by the operations service, and reports what it did. A
    /// refusal (no ids, more than the service's batch limit) is shown as <see cref="BatchError"/>
    /// and leaves the selection and polling as they are, so the operator can change the selection
    /// and try again. An accepted batch is reported with the service's one-line count, as a
    /// warning when it triggered nothing or noted an id it could not trigger as asked; each note
    /// is shown as <see cref="BatchError"/>. Then <paramref name="clearSelection"/> runs, because
    /// the ids that were triggered must not be sent again, polling resumes and the data reloads.
    /// An exception is shown as <see cref="BatchError"/>.
    /// </summary>
    /// <param name="summary">The notification's title, such as "Batch Trigger".</param>
    /// <param name="operation">The service call.</param>
    /// <param name="clearSelection">Empties the page's selection.</param>
    private protected async Task RunBatchTriggerAsync(
        string summary,
        Func<Task<BatchTriggerResult>> operation,
        Action clearSelection
    )
    {
        BatchError = null;
        BatchOperating = true;

        try
        {
            var result = await operation();
            if (!result.Success)
            {
                BatchError = result.Message;
                return;
            }

            var triggered = result.Queued + result.AlreadyQueued + result.TooLateToAskAfresh;
            BatchNotifications.Notify(
                triggered == 0 || result.Notes.Count > 0
                    ? NotificationSeverity.Warning
                    : NotificationSeverity.Success,
                summary,
                result.Message,
                duration: 6000
            );
            if (result.Notes.Count > 0)
                BatchError = string.Join(" ", result.Notes.Select(n => n.Message));

            clearSelection();
            PausePolling = false;
            await LoadDataAsync(DisposalToken);
        }
        catch (Exception ex)
        {
            BatchError = ex.Message;
        }
        finally
        {
            BatchOperating = false;
        }
    }

    /// <summary>
    /// A CancellationToken that is cancelled when the component is disposed.
    /// Event handlers can pass this to async operations so they abort when the user navigates away.
    /// </summary>
    private protected CancellationToken DisposalToken => _cts?.Token ?? CancellationToken.None;

    /// <summary>
    /// Loads the page's data into component state. Called once on initialization, on every
    /// poll tick that is not paused, on <see cref="RefreshNowAsync"/>, and after a successful
    /// either <c>RunBatchOperationAsync</c>. Background ticks run it on the renderer's
    /// synchronization context and re-render afterwards.
    /// </summary>
    /// <param name="cancellationToken">
    /// Cancelled when the component is disposed or a newer refresh supersedes this one.
    /// </param>
    private protected abstract Task LoadDataAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Override in pages with route parameters (e.g. <c>{Id:int}</c>) to return a value
    /// that uniquely identifies the current route. When this value changes between renders,
    /// the component cancels the current poll cycle, reloads data immediately, and restarts polling.
    /// </summary>
    private protected virtual object? GetRouteKey() => null;

    /// <summary>
    /// Called when <see cref="GetRouteKey"/> changes, before the reload for the new route. Override
    /// to drop state that belongs to the previous route (the loaded row, unsaved edits), so a
    /// reload that fails does not leave the previous entity on screen under the new URL.
    /// </summary>
    private protected virtual void OnRouteKeyChanged() { }

    /// <summary>
    /// Registers the same-URL navigation handler, initializes <see cref="DashboardSettings"/>,
    /// runs the first <see cref="LoadDataAsync"/> and starts the poll loop. A derived page that
    /// overrides this must call the base implementation.
    /// </summary>
    protected override async Task OnInitializedAsync()
    {
        _locationChangingRegistration = NavigationManager.RegisterLocationChangingHandler(
            OnLocationChanging
        );

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        await DashboardSettings.InitializeAsync();

        if (token.IsCancellationRequested)
            return;

        await LoadOnceAsync(token);

        if (token.IsCancellationRequested)
            return;

        IsLoading = false;

        _lastRouteKey = GetRouteKey();

        _ = PollAsync(token);
    }

    /// <summary>
    /// Reloads immediately, showing the loading state, when <see cref="GetRouteKey"/> returns a
    /// different value than it did for the previous render. A derived page that overrides this
    /// must call the base implementation.
    /// </summary>
    protected override async Task OnParametersSetAsync()
    {
        var key = GetRouteKey();

        if (_lastRouteKey is not null && !Equals(key, _lastRouteKey))
        {
            OnRouteKeyChanged();
            await RefreshNowAsync(showLoading: true);
        }

        _lastRouteKey = key;
    }

    /// <summary>
    /// Intercepts same-URL navigation (sidebar re-click, DAG node click on current entity)
    /// and converts it into a data refresh instead of a no-op.
    /// </summary>
    private ValueTask OnLocationChanging(LocationChangingContext context)
    {
        var current = NavigationManager.ToBaseRelativePath(NavigationManager.Uri);

        var target = Uri.TryCreate(context.TargetLocation, UriKind.Absolute, out _)
            ? NavigationManager.ToBaseRelativePath(context.TargetLocation)
            : context.TargetLocation.TrimStart('/');

        if (string.Equals(current, target, StringComparison.OrdinalIgnoreCase))
        {
            context.PreventNavigation();
            _ = InvokeAsync(() => RefreshNowAsync());
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Cancels the current poll cycle, loads data immediately, and restarts the poll loop.
    /// Call this from event handlers (e.g. button clicks, graph node clicks) to force an
    /// immediate refresh without waiting for the next tick.
    /// </summary>
    private protected async Task RefreshNowAsync(bool showLoading = false)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        if (showLoading)
        {
            IsLoading = true;
            StateHasChanged();
        }

        if (token.IsCancellationRequested)
            return;

        await LoadOnceAsync(token);

        if (token.IsCancellationRequested)
            return;

        IsLoading = false;

        _ = PollAsync(token);
    }

    /// <summary>
    /// Runs one load outside the poll loop, recording a failure the way a failed tick is recorded
    /// instead of letting it out of the calling lifecycle method or event handler. A load this
    /// component cancelled (superseded or disposed) ends quietly.
    /// </summary>
    private async Task LoadOnceAsync(CancellationToken token)
    {
        try
        {
            await LoadDataAsync(token);
            LoadError = null;
            DashboardSettings.NotifyPolled();
        }
        catch (Exception) when (token.IsCancellationRequested)
        {
            // Superseded by a newer load or disposed: the newer load owns the outcome.
        }
        catch (Exception ex)
        {
            LoadError = ex.Message;
            DashboardSettings.NotifyPollFailed(ex.Message);
        }
    }

    private async Task PollAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(DashboardSettings.PollingInterval, ct);

                if (PausePolling)
                    continue;

                try
                {
                    await InvokeAsync(async () =>
                    {
                        await LoadDataAsync(ct);
                        LoadError = null;
                        DashboardSettings.NotifyPolled();
                        StateHasChanged();
                    });
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    // Keep polling: the next tick may succeed. Until one does, the header says
                    // the rows on screen are from the last refresh that worked.
                    LoadError = ex.Message;
                    DashboardSettings.NotifyPollFailed(ex.Message);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Component disposed — exit gracefully.
        }
    }

    /// <summary>
    /// Stops the poll loop, cancels <see cref="DisposalToken"/> and unregisters the navigation
    /// handler. An override must call the base implementation.
    /// </summary>
    public virtual ValueTask DisposeAsync()
    {
        _locationChangingRegistration?.Dispose();
        _cts?.Cancel();
        _cts?.Dispose();
        return ValueTask.CompletedTask;
    }
}
