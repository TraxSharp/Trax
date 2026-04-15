using HotChocolate.Execution.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;

namespace Trax.Api.GraphQL.Audit;

/// <summary>
/// Fluent extension on <see cref="TraxGraphQLBuilder"/> that registers the
/// audit channel, listener, background writer, options, and sink.
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// </remarks>
public static class TraxGraphQLBuilderAuditExtensions
{
    /// <summary>
    /// Wires the Trax GraphQL audit pipeline: registers the singleton channel,
    /// the hosted background writer, the sink <typeparamref name="TSink"/>, and
    /// the diagnostic listener that captures each request.
    /// </summary>
    /// <typeparam name="TSink">Consumer-provided sink implementation.</typeparam>
    /// <param name="builder">The Trax GraphQL builder.</param>
    /// <param name="configure">Optional hook for tweaking <see cref="TraxAuditOptions"/>.</param>
    /// <remarks>
    /// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
    /// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
    /// </remarks>
    public static TraxGraphQLBuilder AddAudit<TSink>(
        this TraxGraphQLBuilder builder,
        Action<TraxAuditOptions>? configure = null
    )
        where TSink : class, ITraxAuditSink
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;

        services.AddHttpContextAccessor();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<TraxAuditChannel>();
        services.AddScoped<ITraxAuditSink, TSink>();
        services.TryAddSingleton<ITraxAuditRedactor, DefaultAuditRedactor>();
        services.AddSingleton<TraxGraphQLAuditListener>();
        services.AddSingleton<IHostedService, TraxAuditWriter>();

        if (configure is not null)
            services.Configure(configure);
        else
            services.AddOptions<TraxAuditOptions>();

        EnsureDisclaimerLog(services);

        builder.ConfigureSchema(schema =>
            schema.AddDiagnosticEventListener<TraxGraphQLAuditListener>()
        );

        return builder;
    }

    private static void EnsureDisclaimerLog(IServiceCollection services)
    {
        if (services.Any(sd => sd.ImplementationType == typeof(TraxAuditDisclaimerHostedService)))
            return;

        services.AddSingleton<IHostedService, TraxAuditDisclaimerHostedService>();
    }
}

internal sealed class TraxAuditDisclaimerHostedService(ILoggerFactory loggerFactory)
    : IHostedService
{
    private const string Message =
        "Trax auth enabled. Trax provides NO WARRANTY for security breaches against systems using this package. You are solely responsible. See SECURITY-DISCLAIMER.md.";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        loggerFactory.CreateLogger("Trax.Api.GraphQL.Audit").LogWarning(Message);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
