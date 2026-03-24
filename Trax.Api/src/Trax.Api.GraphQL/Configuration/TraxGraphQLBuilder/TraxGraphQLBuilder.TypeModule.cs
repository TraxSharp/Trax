namespace Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;

public partial class TraxGraphQLBuilder
{
    /// <summary>
    /// Registers an additional HotChocolate <see cref="HotChocolate.Execution.Configuration.TypeModule"/>
    /// to be added to the Trax GraphQL schema. Use this to extend entity types with
    /// custom resolvers, DataLoader-backed relationship fields, or other type extensions.
    /// </summary>
    /// <typeparam name="TTypeModule">
    /// A <see cref="HotChocolate.Execution.Configuration.TypeModule"/> implementation. It will be registered
    /// as a singleton in DI and added to the HotChocolate schema builder.
    /// </typeparam>
    public TraxGraphQLBuilder AddTypeModule<TTypeModule>()
        where TTypeModule : HotChocolate.Execution.Configuration.TypeModule
    {
        AdditionalTypeModules.Add(typeof(TTypeModule));
        return this;
    }
}
