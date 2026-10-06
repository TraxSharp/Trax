namespace Trax.Api.GraphQL.Queries;

/// <summary>
/// Root query type. Empty by default. Sub-namespaces are added dynamically:
/// <list type="bullet">
///   <item><c>operations</c> — added by <c>TraxGraphQLBuilder.ExposeOperationQueries()</c>.</item>
///   <item><c>discover</c> — added by <see cref="TypeModules.TrainTypeModule"/> when trains
///     annotated with <c>[TraxQuery]</c> or query-model registrations are present.</item>
/// </list>
/// </summary>
public class RootQuery { }
