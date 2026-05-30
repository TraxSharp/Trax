using Trax.Api.GraphQL.Filtering;

namespace Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;

public partial class TraxGraphQLBuilder
{
    internal List<ITraxFilterModule> FilterModules { get; } = [];

    /// <summary>
    /// Configures Trax's filter convention. HotChocolate's stock filtering is the
    /// default; this layers additive operators on top of it. Nothing changes for
    /// existing consumers unless this is called.
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddTraxGraphQL(graphql => graphql
    ///     .AddDbContext&lt;GameDbContext&gt;()
    ///     .ConfigureFiltering(filter => filter.AddCaseInsensitiveStringOperations()));
    /// </code>
    /// </example>
    public TraxGraphQLBuilder ConfigureFiltering(
        Func<TraxFilterBuilder.TraxFilterBuilder, TraxFilterBuilder.TraxFilterBuilder> configure
    )
    {
        var filterBuilder = new TraxFilterBuilder.TraxFilterBuilder();
        configure(filterBuilder);

        // Dedup by module type so calling ConfigureFiltering more than once stays safe:
        // a module registers fixed operation names, and HotChocolate throws if the same
        // name is added twice.
        foreach (var module in filterBuilder.Build())
            if (FilterModules.All(existing => existing.GetType() != module.GetType()))
                FilterModules.Add(module);

        return this;
    }
}
