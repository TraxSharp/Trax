using Microsoft.AspNetCore.Components;
using Trax.Dashboard.Configuration;
using Trax.Dashboard.Services.Authorization;

namespace Trax.Dashboard.Components.Shared;

/// <summary>
/// Holds the dashboard circuit to its authorization posture for as long as it lives: attaches the
/// circuit, re-checks the posture on an interval, and reloads the page through the dashboard's
/// endpoint authorization once the user no longer satisfies it. Rendered once, by the dashboard's
/// root component. Part of the dashboard UI; not intended to be used directly.
/// </summary>
public sealed class DashboardAuthorizationMonitor : ComponentBase, IDisposable
{
    private readonly CancellationTokenSource _cts = new();

    [Inject]
    private DashboardCircuitAuthorization Authorization { get; set; } = default!;

    [Inject]
    private DashboardOptions Options { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    /// <summary>Attaches the circuit and starts the interval re-check.</summary>
    protected override void OnInitialized()
    {
        Authorization.Refused += OnRefused;
        Authorization.Attach();
        if (Authorization.IsRefused)
        {
            OnRefused();
            return;
        }

        if (!Options.AnonymousAllowed)
            _ = RevalidateOnIntervalAsync(_cts.Token);
    }

    /// <summary>Stops the interval re-check and detaches from the circuit's verdict.</summary>
    public void Dispose()
    {
        Authorization.Refused -= OnRefused;
        _cts.Cancel();
        _cts.Dispose();
    }

    private async Task RevalidateOnIntervalAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(Options.AuthorizationRevalidationInterval);
            while (await timer.WaitForNextTickAsync(ct))
            {
                if (!await Authorization.RevalidateAsync())
                    return;
            }
        }
        catch (OperationCanceledException)
        {
            // The circuit closed.
        }
    }

    // A reload goes back through the endpoint posture, which challenges or forbids the user.
    // The circuit handler refuses anything this circuit sends meanwhile, so a client that ignores
    // the navigation gets nowhere either.
    private void OnRefused() =>
        _ = InvokeAsync(() => Navigation.NavigateTo(Navigation.Uri, forceLoad: true));
}
