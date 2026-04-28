namespace Trax.Api.GraphQL.Mutations;

/// <summary>
/// Root mutation type. Empty by default. Sub-namespaces are added dynamically:
/// <list type="bullet">
///   <item><c>operations</c> — added by <c>TraxGraphQLBuilder.ExposeOperationMutations()</c>.</item>
///   <item><c>dispatch</c> — added by <see cref="TypeModules.TrainTypeModule"/> when trains
///     annotated with <c>[TraxMutation]</c> are registered.</item>
/// </list>
/// </summary>
public class RootMutation { }
