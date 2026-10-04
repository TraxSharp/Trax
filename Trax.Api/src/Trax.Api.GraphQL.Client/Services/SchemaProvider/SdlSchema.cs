using GraphQL;
using GraphQL.Resolvers;
using GraphQL.Types;
using GraphQLParser;
using GraphQLParser.AST;
using GraphQLParser.Visitors;

namespace Trax.Api.GraphQL.Client;

/// <summary>
/// The one step every schema provider takes to turn SDL into a graphql-dotnet
/// <see cref="ISchema"/>: drop the subscription root when the client asks for that, and back every
/// custom scalar the SDL declares (HotChocolate's <c>Any</c>, <c>UUID</c>, <c>URL</c>,
/// <c>DateTime</c>, ...) with a <see cref="PermissiveScalarGraphType"/>. graphql-dotnet knows only
/// the five spec scalars, so without the second step a schema that declares any other fails to
/// initialise with <c>Unable to resolve reference to type</c> the first time a query is validated.
/// </summary>
internal static class SdlSchema
{
    /// <summary>Builds the schema from <paramref name="sdl"/>.</summary>
    /// <param name="sdl">The schema's SDL.</param>
    /// <param name="removeSubscriptions">Whether the subscription root type is dropped first.</param>
    /// <exception cref="GraphQLParser.Exceptions.GraphQLSyntaxErrorException">The SDL does not parse.</exception>
    public static ISchema Build(string sdl, bool removeSubscriptions)
    {
        var document = Parser.Parse(sdl);

        if (removeSubscriptions && RemoveSubscriptionRoot(document))
            sdl = new SDLPrinter().Print(document);

        var schema = Schema.For(sdl);
        var scalars = CustomScalarNames(document)
            .Select(name => (IGraphType)new PermissiveScalarGraphType(name))
            .ToArray();
        if (scalars.Length > 0)
            schema.RegisterTypes(scalars);

        // graphql-dotnet refuses a subscription field with no stream resolver. The client never
        // executes one, so a kept subscription root gets a resolver that says so.
        if (schema.Subscription is { } subscription)
            foreach (var field in subscription.Fields)
                field.StreamResolver ??= NotExecutedStream.Instance;

        // Initialise now, so an SDL graphql-dotnet cannot use fails the load (which is retried)
        // rather than the first validation that touches it.
        schema.Initialize();
        return schema;
    }

    /// <summary>
    /// The scalars the SDL declares beyond the five the specification defines.
    /// </summary>
    internal static IEnumerable<string> CustomScalarNames(GraphQLDocument document) =>
        document
            .Definitions.OfType<GraphQLScalarTypeDefinition>()
            .Select(scalar => scalar.Name.StringValue)
            .Where(name =>
                !IntrospectionSdlBuilder.IsBuiltinScalar(name)
                && !name.StartsWith("__", StringComparison.Ordinal)
            )
            .Distinct(StringComparer.Ordinal);

    /// <summary>
    /// Removes the subscription root: its entry in the schema definition, or the type named
    /// <c>Subscription</c> when the SDL has no schema definition, and that type's definition and
    /// extensions. Returns whether anything was removed.
    /// </summary>
    private static bool RemoveSubscriptionRoot(GraphQLDocument document)
    {
        string? rootName = null;
        var schemaDefinition = document
            .Definitions.OfType<GraphQLSchemaDefinition>()
            .FirstOrDefault();

        if (schemaDefinition is not null)
        {
            var entry = schemaDefinition.OperationTypes.FirstOrDefault(o =>
                o.Operation == OperationType.Subscription
            );
            if (entry is not null)
            {
                rootName = entry.Type!.Name.StringValue;
                schemaDefinition.OperationTypes.Remove(entry);
            }
        }
        else
        {
            rootName = "Subscription";
        }

        if (rootName is null)
            return false;

        var removed = document.Definitions.RemoveAll(definition =>
            definition switch
            {
                GraphQLObjectTypeDefinition type => type.Name.StringValue == rootName,
                GraphQLObjectTypeExtension extension => extension.Name.StringValue == rootName,
                _ => false,
            }
        );

        return schemaDefinition is not null || removed > 0;
    }

    private sealed class NotExecutedStream : ISourceStreamResolver
    {
        public static readonly NotExecutedStream Instance = new();

        public ValueTask<IObservable<object?>> ResolveAsync(IResolveFieldContext context) =>
            throw new NotSupportedException(
                "A client-side schema is used to validate operations, never to execute them."
            );
    }
}
