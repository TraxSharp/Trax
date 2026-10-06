using HotChocolate.Execution.Configuration;

namespace Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;

public partial class TraxGraphQLBuilder
{
    /// <summary>
    /// Applies arbitrary configuration to the underlying HotChocolate
    /// <see cref="IRequestExecutorBuilder"/>. Use this for settings that
    /// Trax does not expose directly, such as cost analysis options,
    /// custom conventions, or additional middleware.
    /// </summary>
    /// <param name="configure">
    /// A callback that receives the <see cref="IRequestExecutorBuilder"/>
    /// after all standard Trax configuration has been applied.
    /// </param>
    public TraxGraphQLBuilder ConfigureSchema(Action<IRequestExecutorBuilder> configure)
    {
        SchemaConfigurations.Add(configure);
        return this;
    }
}
