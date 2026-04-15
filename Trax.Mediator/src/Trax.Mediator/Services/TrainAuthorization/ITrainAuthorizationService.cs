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
    Task AuthorizeAsync(TrainRegistration registration, CancellationToken ct = default);
}
