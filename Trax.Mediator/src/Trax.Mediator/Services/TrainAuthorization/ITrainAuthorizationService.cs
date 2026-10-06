using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Mediator.Services.TrainAuthorization;

/// <summary>
/// Checks whether the current caller is authorized to execute a given train.
/// </summary>
/// <remarks>
/// Implementations should return normally when authorization succeeds,
/// and throw when it fails. The default implementation in Trax.Api uses
/// ASP.NET Core's <c>IAuthorizationService</c> and <c>IHttpContextAccessor</c>
/// to evaluate <see cref="Trax.Effect.Attributes.TraxAuthorizeAttribute"/>
/// requirements against the current HTTP user.
///
/// The execution service requires this service to be registered whenever any
/// registered train carries <see cref="Trax.Effect.Attributes.TraxAuthorizeAttribute"/>.
/// Scheduler-only or dashboard-only hosts that never serve API submissions can opt out via
/// <c>TraxMediatorBuilder.AllowMissingAuthorizationService()</c>.
/// </remarks>
public interface ITrainAuthorizationService
{
    /// <summary>
    /// Returns when the current caller may run or queue <paramref name="registration"/>, and throws
    /// when it may not. Called by <c>ITrainExecutionService</c> for every train before the input is
    /// read, including trains without <c>[TraxAuthorize]</c>, and inside a trusted execution scope:
    /// the mediator does not check <see cref="TrustedExecution.ITrustedExecutionScope.IsTrusted"/>
    /// before calling, so an implementation decides itself whether trust skips its checks.
    /// </summary>
    /// <param name="registration">
    /// The train being submitted; <see cref="TrainRegistration.RequiredPolicies"/> (every one must
    /// pass), <see cref="TrainRegistration.RequiredRoleSets"/> (one role of every set must be held)
    /// and <see cref="TrainRegistration.HasAuthorizeAttribute"/> carry its requirements.
    /// <see cref="TrainRegistration.RequiredRoles"/> is only their union, and checking it as
    /// any-of admits a caller one of the train's attributes refuses.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="UnauthorizedAccessException">
    /// The expected way to refuse; Trax.Api's <c>TrainAuthorizationException</c> derives from it,
    /// and surfaces map it to an authorization error.
    /// </exception>
    Task AuthorizeAsync(TrainRegistration registration, CancellationToken ct = default);
}
