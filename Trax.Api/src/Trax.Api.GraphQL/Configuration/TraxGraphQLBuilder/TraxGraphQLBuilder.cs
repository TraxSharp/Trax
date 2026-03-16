using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;

/// <summary>
/// Builder for configuring the Trax GraphQL schema, including DbContext-based
/// model query registration.
/// </summary>
public partial class TraxGraphQLBuilder
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal IServiceCollection Services { get; }

    internal List<Type> DbContextTypes { get; } = [];

    public TraxGraphQLBuilder(IServiceCollection services)
    {
        Services = services;
    }
}
