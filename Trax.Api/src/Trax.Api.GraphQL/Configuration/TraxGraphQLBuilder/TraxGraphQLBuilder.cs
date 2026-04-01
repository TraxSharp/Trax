using System.ComponentModel;
using HotChocolate.Execution.Configuration;
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

    internal List<Type> AdditionalTypeModules { get; } = [];

    internal List<Type> AdditionalTypeExtensions { get; } = [];

    internal Dictionary<Type, Type> FilterTypeOverrides { get; } = [];

    internal Dictionary<Type, Type> SortTypeOverrides { get; } = [];

    internal List<Action<IRequestExecutorBuilder>> SchemaConfigurations { get; } = [];

    public TraxGraphQLBuilder(IServiceCollection services)
    {
        Services = services;
    }
}
