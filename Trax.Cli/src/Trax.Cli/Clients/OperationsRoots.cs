using System.Security.Cryptography;
using System.Text;
using GraphQLParser.AST;
using Trax.Core.Extensions;

namespace Trax.Cli.Clients;

/// <summary>The root types an operations schema names in its <c>schema</c> block.</summary>
/// <param name="Query">The query root's type.</param>
/// <param name="Mutation">The mutation root's type.</param>
/// <param name="Subscription">The subscription root's type.</param>
internal sealed record OperationsRoots(string Query, string Mutation, string Subscription)
{
    /// <summary>The roots <paramref name="sdl"/> declares, or GraphQL's default names for those it does not.</summary>
    public static OperationsRoots Of(GraphQLDocument sdl)
    {
        var declared = sdl
            .Definitions.OfType<GraphQLSchemaDefinition>()
            .SelectMany(s => s.OperationTypes)
            .ToDictionary(o => o.Operation, o => o.Type.NotNull().Name.StringValue);
        return new(
            declared.GetValueOrDefault(OperationType.Query, "Query"),
            declared.GetValueOrDefault(OperationType.Mutation, "Mutation"),
            declared.GetValueOrDefault(OperationType.Subscription, "Subscription")
        );
    }
}
