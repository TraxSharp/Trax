using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Trax.Api.Tests.Fakes;

/// <summary>
/// Registers a Development <see cref="IHostEnvironment"/> for tests that read the schema by
/// running introspection in-process. A request built in-process carries no HTTP request, so
/// Trax answers it by the environment alone and allows introspection only in Development.
/// </summary>
internal static class DevelopmentEnvironment
{
    public static IServiceCollection AddDevelopmentEnvironment(this IServiceCollection services)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Development);
        services.AddSingleton(environment);
        return services;
    }
}
