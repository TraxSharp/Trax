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
/// This service is resolved optionally by <see cref="TrainExecution.TrainExecutionService"/>.
/// When not registered (e.g., scheduler-only or dashboard-only setups),
/// no authorization checks are performed.
/// </remarks>
public interface ITrainAuthorizationService
{
    Task AuthorizeAsync(TrainRegistration registration, CancellationToken ct = default);
}
