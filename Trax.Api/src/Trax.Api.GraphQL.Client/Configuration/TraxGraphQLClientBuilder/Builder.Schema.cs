using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Trax.Api.GraphQL.Client;

public sealed partial class TraxGraphQLClientBuilder
{
    /// <summary>
    /// Explicitly use <see cref="IntrospectingSchemaProvider"/>. This is the default when no
    /// <c>Use*Schema</c> method is called, so this method exists for readability rather than
    /// necessity ("yes, I really do want introspection").
    /// </summary>
    public TraxGraphQLClientBuilder UseIntrospection()
    {
        Services.Replace(
            ServiceDescriptor.Singleton<ISchemaProvider, IntrospectingSchemaProvider>()
        );
        return this;
    }

    /// <summary>
    /// Load the schema from a checked-in SDL file (typically <c>schema.graphql</c>). Use this
    /// when the live endpoint isn't reachable at startup (CI, air-gapped) or when you want
    /// validation against a known-good snapshot rather than the endpoint's current state.
    /// Pair with a periodic introspection-snapshot job to detect drift between the file and
    /// the live server.
    /// </summary>
    /// <param name="sdlPath">
    /// Absolute or relative path to the SDL file. Resolved at the time the schema is first
    /// requested by the validator, not at registration time.
    /// </param>
    public TraxGraphQLClientBuilder UseFileSchema(string sdlPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sdlPath);
        Services.Replace(
            ServiceDescriptor.Singleton<ISchemaProvider>(_ => new FileSchemaProvider(sdlPath))
        );
        return this;
    }
}
