using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.GraphQL.Configuration;

namespace Trax.Api.GraphQL.Startup;

/// <summary>
/// Fails the host at startup when a type extension added a field that inherits no authorization
/// gate and declares none. Materialising the schema is what runs
/// <see cref="TypeExtensionExposureInterceptor"/>, so the check has to own a schema build rather
/// than read a registration list.
/// </summary>
/// <remarks>
/// Reports every offending field in one message. The alternative, throwing from inside the
/// interceptor, surfaces as a HotChocolate <c>SchemaException</c> naming one field at a time, and
/// arrives on the first request rather than at startup unless something else already builds the
/// schema.
/// </remarks>
internal sealed class TypeExtensionExposureValidator(
    TypeExtensionExposureReport report,
    IServiceProvider serviceProvider
) : IHostedService
{
    /// <summary>Schema name registered by Trax for the GraphQL endpoint.</summary>
    private const string TraxSchemaName = "trax";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IRequestExecutorProvider>();

        // Building the schema is the check: the interceptor writes into the report while this
        // runs.
        await provider.GetExecutorAsync(TraxSchemaName, cancellationToken);

        var violations = report.Violations;
        if (violations.Count == 0)
            return;

        throw new InvalidOperationException(
            $"{violations.Count} GraphQL field(s) added by a type extension have no authorization "
                + "posture:"
                + Environment.NewLine
                + Environment.NewLine
                + string.Join(
                    Environment.NewLine + Environment.NewLine,
                    violations.Select(v => v.Message)
                )
        );
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
