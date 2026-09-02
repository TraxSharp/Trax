using HotChocolate.Execution.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Trax.Api.GraphQL.Extensions;

/// <summary>
/// Bridges application-container services into HotChocolate's schema container.
/// </summary>
/// <remarks>
/// HotChocolate 16 stopped forwarding unresolved schema-container lookups to the
/// application container, so anything HotChocolate activates itself — request
/// interceptors, socket-session interceptors, diagnostic listeners — can no longer see
/// the host's services. This re-exposes one of them.
/// <para>
/// The bridge resolves through the application provider on first use rather than while
/// the schema container is being built. That matters twice over. It keeps
/// <c>AddTraxGraphQL()</c> independent of registration order, so a host is free to call
/// <c>AddAuthentication()</c> after it (HotChocolate 15 forwarded lookups at request time,
/// so order never mattered before and consumers rely on that). And it keeps a service the
/// host never registered from turning into a startup failure: it fails only if something
/// actually asks for it, naming the missing type, which is what HotChocolate 15 did.
/// </para>
/// </remarks>
internal static class ApplicationServiceBridge
{
    /// <summary>
    /// Makes <typeparamref name="TService"/> resolvable from the schema container by
    /// forwarding to the application container.
    /// </summary>
    public static IRequestExecutorBuilder BridgeApplicationService<TService>(
        this IRequestExecutorBuilder builder
    )
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.ConfigureSchemaServices(
            (applicationServices, schemaServices) =>
                schemaServices.AddSingleton(_ => applicationServices.GetRequiredService<TService>())
        );
    }
}
