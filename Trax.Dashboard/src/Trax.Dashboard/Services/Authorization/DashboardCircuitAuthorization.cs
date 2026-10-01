using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Dashboard.Configuration;

namespace Trax.Dashboard.Services.Authorization;

/// <summary>
/// Re-checks the dashboard's authorization posture inside an established circuit. The endpoint
/// posture is checked only on the page request and the hub's connect; this keeps checking it for
/// as long as the circuit lives, against whatever <see cref="AuthenticationStateProvider"/> the
/// host registered (the dashboard registers none of its own).
/// </summary>
/// <remarks>
/// <para>
/// One instance per circuit. A circuit takes part once the dashboard's root component calls
/// <see cref="Attach"/>, so a host's own Blazor circuits, if it has any, are left alone.
/// </para>
/// <para>
/// The posture is evaluated when the circuit attaches, whenever the host's provider reports a
/// change, on <see cref="DashboardOptions.AuthorizationRevalidationInterval"/>, and on demand
/// through <see cref="EnsureAuthorizedAsync"/>. The verdict is read before every inbound circuit
/// message by <see cref="DashboardAuthorizationCircuitHandler"/>. A refusal is final for the
/// circuit: the next inbound message throws, which makes the framework close it, and a reload
/// goes back through the endpoint posture.
/// </para>
/// <para>
/// Any failure to evaluate (no provider, a policy handler that throws) is a refusal.
/// </para>
/// </remarks>
internal sealed class DashboardCircuitAuthorization : IDisposable
{
    private readonly DashboardOptions _options;
    private readonly IServiceProvider _services;
    private readonly ILogger<DashboardCircuitAuthorization> _logger;
    private readonly object _gate = new();

    private AuthenticationStateProvider? _provider;
    private Task<bool>? _pending;
    private volatile bool _attached;
    private volatile bool _refused;

    public DashboardCircuitAuthorization(
        DashboardOptions options,
        IServiceProvider services,
        ILogger<DashboardCircuitAuthorization> logger
    )
    {
        _options = options;
        _services = services;
        _logger = logger;
    }

    /// <summary>Raised once, when this circuit's user stops satisfying the posture.</summary>
    public event Action? Refused;

    /// <summary>Whether this circuit has been refused. Once set it stays set.</summary>
    public bool IsRefused => _refused;

    /// <summary>
    /// Makes this circuit a dashboard circuit: from now on its user is held to the posture.
    /// Idempotent. Starts an evaluation without waiting for it.
    /// </summary>
    public void Attach()
    {
        lock (_gate)
        {
            if (_attached)
                return;
            _attached = true;
        }

        if (_options.AnonymousAllowed)
            return;

        _provider = _services.GetService<AuthenticationStateProvider>();
        if (_provider is null)
        {
            Refuse("the host registers no AuthenticationStateProvider to read the user from");
            return;
        }

        _provider.AuthenticationStateChanged += OnAuthenticationStateChanged;
        _ = RevalidateAsync(stateTask: null);
    }

    /// <summary>
    /// Evaluates the posture now, for an action about to write. Attaches the circuit first, so a
    /// caller can never skip the check by running before the root component did.
    /// </summary>
    /// <returns><see langword="true"/> when the current user still satisfies the posture.</returns>
    public Task<bool> EnsureAuthorizedAsync()
    {
        Attach();
        return RevalidateAsync(stateTask: null);
    }

    /// <summary>Evaluates the posture now, as the interval does.</summary>
    public Task<bool> RevalidateAsync() => RevalidateAsync(stateTask: null);

    /// <summary>
    /// Throws when the circuit has been refused. Called synchronously before each inbound circuit
    /// message, so an evaluation already finished costs nothing and keeps message order.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">The circuit has been refused.</exception>
    public void ThrowIfRefused()
    {
        if (_refused)
            throw new UnauthorizedAccessException(
                "The Trax dashboard closed this circuit: its user no longer satisfies the "
                    + "dashboard's authorization posture. Reload the page to sign in again."
            );
    }

    /// <summary>
    /// The evaluation still running, if any. An inbound message waits for it before going on, so
    /// nothing slips through between an authentication change and its verdict.
    /// </summary>
    public Task? PendingEvaluation
    {
        get
        {
            var pending = Volatile.Read(ref _pending);
            return pending is null || pending.IsCompleted ? null : pending;
        }
    }

    public void Dispose()
    {
        if (_provider is not null)
            _provider.AuthenticationStateChanged -= OnAuthenticationStateChanged;
    }

    private void OnAuthenticationStateChanged(Task<AuthenticationState> stateTask) =>
        _ = RevalidateAsync(stateTask);

    private async Task<bool> RevalidateAsync(Task<AuthenticationState>? stateTask)
    {
        if (_refused)
            return false;
        if (!_attached || _options.AnonymousAllowed)
            return true;

        var evaluation = EvaluateAsync(stateTask);
        Volatile.Write(ref _pending, evaluation);
        if (!await evaluation.ConfigureAwait(false))
            Refuse("the user no longer satisfies the dashboard's posture");
        return !_refused;
    }

    private async Task<bool> EvaluateAsync(Task<AuthenticationState>? stateTask)
    {
        try
        {
            if (_provider is null)
                return false;

            var state = await (stateTask ?? _provider.GetAuthenticationStateAsync()).ConfigureAwait(
                false
            );

            // The same policy UseTraxDashboard() puts on the endpoints, built the way the
            // authorization middleware builds it from that attribute.
            var policy = await AuthorizationPolicy
                .CombineAsync(
                    _services.GetRequiredService<IAuthorizationPolicyProvider>(),
                    [
                        new AuthorizeAttribute
                        {
                            Policy = _options.Policy,
                            Roles = _options.Roles is null
                                ? null
                                : string.Join(",", _options.Roles),
                        },
                    ]
                )
                .ConfigureAwait(false);
            if (policy is null)
                return false;

            var result = await _services
                .GetRequiredService<IAuthorizationService>()
                .AuthorizeAsync(state.User, resource: null, policy)
                .ConfigureAwait(false);
            return result.Succeeded;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "The Trax dashboard could not re-check its authorization posture for a circuit, "
                    + "and refuses it."
            );
            return false;
        }
    }

    private void Refuse(string why)
    {
        lock (_gate)
        {
            if (_refused)
                return;
            _refused = true;
        }

        _logger.LogWarning(
            "The Trax dashboard is closing a circuit: {Reason}. The user has to reload, which "
                + "goes back through the dashboard's endpoint authorization.",
            why
        );
        Refused?.Invoke();
    }
}
