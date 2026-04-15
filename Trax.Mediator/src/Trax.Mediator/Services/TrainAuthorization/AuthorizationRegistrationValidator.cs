using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Effect.Attributes;
using Trax.Mediator.Configuration;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Mediator.Services.TrainAuthorization;

/// <summary>
/// Fail-closed startup checks. Validates that:
/// <list type="bullet">
/// <item>
/// No <c>[TraxAuthorize]</c>-gated train is registered without a matching
/// <see cref="ITrainAuthorizationService"/> (opt out via
/// <c>TraxMediatorBuilder.AllowMissingAuthorizationService()</c>).
/// </item>
/// <item>
/// Every <c>[TraxAuthorize]</c> attribute has a well-formed shape: non-whitespace
/// Policy when present, and Roles that parse to one or more non-empty entries
/// when present.
/// </item>
/// </list>
/// </summary>
internal sealed class AuthorizationRegistrationValidator(
    ITrainDiscoveryService discoveryService,
    MediatorConfiguration configuration,
    IServiceProvider serviceProvider
) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var registrations = discoveryService.DiscoverTrains();

        ValidateAttributeShapes(registrations);
        ValidateAuthServicePresence(registrations);

        return Task.CompletedTask;
    }

    private void ValidateAuthServicePresence(IReadOnlyList<TrainRegistration> registrations)
    {
        if (configuration.AllowMissingAuthorizationService)
            return;

        var authService = serviceProvider.GetService<ITrainAuthorizationService>();
        if (authService is not null)
            return;

        var authorizedTrains = registrations
            .Where(t => t.HasAuthorizeAttribute)
            .Select(t => t.ServiceTypeName)
            .ToList();

        if (authorizedTrains.Count == 0)
            return;

        throw new InvalidOperationException(
            "One or more registered trains declare [TraxAuthorize] but no "
                + "ITrainAuthorizationService is registered in the DI container. "
                + "Call services.AddTraxApi() (or register a custom ITrainAuthorizationService) "
                + "before building the host. If this process intentionally runs no API submissions "
                + "(for example a scheduler-only worker), opt out via "
                + "AddMediator(m => m.AllowMissingAuthorizationService()). "
                + $"Authorized trains: {string.Join(", ", authorizedTrains)}."
        );
    }

    private static void ValidateAttributeShapes(IReadOnlyList<TrainRegistration> registrations)
    {
        foreach (var registration in registrations.Where(r => r.HasAuthorizeAttribute))
        {
            var carriers = new List<Type> { registration.ImplementationType };
            carriers.AddRange(registration.ImplementationType.GetInterfaces());

            foreach (var type in carriers)
            {
                var attributes = type.GetCustomAttributes<TraxAuthorizeAttribute>(inherit: true);
                foreach (var attribute in attributes)
                {
                    if (attribute.Policy is not null && string.IsNullOrWhiteSpace(attribute.Policy))
                        throw new InvalidOperationException(
                            $"[TraxAuthorize] on '{type.FullName}' has an empty or whitespace "
                                + "Policy value. Remove the parameter or provide a real policy name."
                        );

                    if (
                        attribute.Roles is not null
                        && attribute
                            .Roles.Split(',', StringSplitOptions.TrimEntries)
                            .All(string.IsNullOrEmpty)
                    )
                        throw new InvalidOperationException(
                            $"[TraxAuthorize(Roles=\"{attribute.Roles}\")] on '{type.FullName}' "
                                + "parsed to zero roles after splitting on ','. Remove the Roles "
                                + "argument or provide one or more non-empty role names."
                        );
                }
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
