using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Trax.Dashboard.Services.DashboardSettings;

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
/// </summary>
public abstract class PollingComponentBase : ComponentBase, IAsyncDisposable
{
    [Inject]
    protected IDashboardSettingsService DashboardSettings { get; set; } = default!;

    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    private CancellationTokenSource? _cts;
    private object? _lastRouteKey;
    private IDisposable? _locationChangingRegistration;

    protected bool IsLoading { get; set; } = true;

    /// <summary>
    /// When true, the polling loop skips data refreshes until the value is set back to false.
    /// Use this to prevent data reloads while the user has an active selection or is in the
    /// middle of an interaction (e.g. batch operations with checkboxes).
    /// The next poll tick after the flag is cleared will refresh normally.
    /// </summary>
    protected bool PausePolling { get; set; }

    /// <summary>
    /// Error message from the most recent batch operation, displayed as an alert.
    /// Cleared at the start of each batch operation.
    /// </summary>
    protected string? BatchError { get; set; }

    /// <summary>
    /// True while a batch operation is in progress. Used to disable action buttons.
    /// </summary>
    protected bool BatchOperating { get; set; }

    /// <summary>
    /// Runs a batch operation with standardized error handling, loading state, and polling control.
    /// Clears <see cref="BatchError"/>, sets <see cref="BatchOperating"/> during execution,
    /// invokes <paramref name="onSuccess"/> on success, unpauses polling, and reloads data.
    /// </summary>
    /// <param name="operation">The async operation to execute.</param>
    /// <param name="onSuccess">Optional callback invoked after the operation succeeds (e.g. clear selection).</param>
    protected async Task RunBatchOperationAsync(Func<Task> operation, Action? onSuccess = null)
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
    /// A CancellationToken that is cancelled when the component is disposed.
    /// Event handlers can pass this to async operations so they abort when the user navigates away.
    /// </summary>
    protected CancellationToken DisposalToken => _cts?.Token ?? CancellationToken.None;

    protected abstract Task LoadDataAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Override in pages with route parameters (e.g. <c>{Id:int}</c>) to return a value
    /// that uniquely identifies the current route. When this value changes between renders,
    /// the component cancels the current poll cycle, reloads data immediately, and restarts polling.
    /// </summary>
    protected virtual object? GetRouteKey() => null;

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

        await LoadDataAsync(token);
        DashboardSettings.NotifyPolled();
        IsLoading = false;

        _lastRouteKey = GetRouteKey();

        _ = PollAsync(token);
    }

    protected override async Task OnParametersSetAsync()
    {
        var key = GetRouteKey();

        if (_lastRouteKey is not null && !Equals(key, _lastRouteKey))
            await RefreshNowAsync(showLoading: true);

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
    protected async Task RefreshNowAsync(bool showLoading = false)
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

        await LoadDataAsync(token);
        DashboardSettings.NotifyPolled();
        IsLoading = false;

        _ = PollAsync(token);
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
                        DashboardSettings.NotifyPolled();
                        StateHasChanged();
                    });
                }
                catch (Exception) when (!ct.IsCancellationRequested)
                {
                    // Transient failure — skip this tick, retry on next interval.
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Component disposed — exit gracefully.
        }
    }

    public virtual ValueTask DisposeAsync()
    {
        _locationChangingRegistration?.Dispose();
        _cts?.Cancel();
        _cts?.Dispose();
        return ValueTask.CompletedTask;
    }
}
