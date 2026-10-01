using Microsoft.AspNetCore.Components.Server.Circuits;

namespace Trax.Dashboard.Services.Authorization;

/// <summary>
/// Refuses every inbound message (a click, a change, a JS interop call, a render acknowledgement)
/// on a dashboard circuit whose user no longer satisfies the posture. Throwing here is what
/// closes the circuit: the framework terminates a circuit whose inbound activity fails, so a
/// client that ignores the dashboard's redirect cannot keep acting.
/// </summary>
/// <remarks>
/// The verdict is read synchronously, so in the ordinary case the message is not delayed and
/// keeps its order relative to the others. Only while an evaluation is running (just after the
/// host's provider reported an authentication change) does a message wait for its result.
/// </remarks>
internal sealed class DashboardAuthorizationCircuitHandler(
    DashboardCircuitAuthorization authorization
) : CircuitHandler
{
    public override Func<CircuitInboundActivityContext, Task> CreateInboundActivityHandler(
        Func<CircuitInboundActivityContext, Task> next
    ) =>
        context =>
        {
            authorization.ThrowIfRefused();
            var pending = authorization.PendingEvaluation;
            return pending is null ? next(context) : AfterEvaluationAsync(pending, next, context);
        };

    private async Task AfterEvaluationAsync(
        Task pending,
        Func<CircuitInboundActivityContext, Task> next,
        CircuitInboundActivityContext context
    )
    {
        await pending.ConfigureAwait(false);
        authorization.ThrowIfRefused();
        await next(context).ConfigureAwait(false);
    }
}
