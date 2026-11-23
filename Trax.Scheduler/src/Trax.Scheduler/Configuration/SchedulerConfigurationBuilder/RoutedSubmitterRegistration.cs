using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Trax.Mediator.Configuration;
using Trax.Scheduler.Services.JobSubmitter;

namespace Trax.Scheduler.Configuration;

/// <summary>
/// Record for tracking a routed submitter registration.
/// Used by extension methods (e.g., <c>UseSqsWorkers()</c>) to register additional submitter backends.
/// </summary>
/// <remarks>
/// A train routed here is submitted through <see cref="CreateSubmitter"/> when it is set, which
/// lets two registrations of the same submitter type (two <c>UseRemoteWorkers</c> calls, for
/// two endpoints) each keep their own options and client. Without it, the submitter is resolved
/// from the container by <see cref="SubmitterType"/>.
/// </remarks>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/scheduler-api/add-scheduler#extension-points">SDK reference</seealso>
[EditorBrowsable(EditorBrowsableState.Never)]
public record RoutedSubmitterRegistration(
    SubmitterRouting Routing,
    Type SubmitterType,
    Action<IServiceCollection> Register
)
{
    /// <summary>
    /// Creates this registration's submitter from a scope's services. Null resolves
    /// <see cref="SubmitterType"/> from the container instead.
    /// </summary>
    public Func<IServiceProvider, IJobSubmitter>? CreateSubmitter { get; init; }

    /// <summary>
    /// A short description of this registration for error messages, such as the endpoint it
    /// submits to. Null uses the submitter type's name.
    /// </summary>
    public string? Description { get; init; }
}
