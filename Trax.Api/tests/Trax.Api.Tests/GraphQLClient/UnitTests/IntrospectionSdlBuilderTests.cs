using FluentAssertions;
using Trax.Api.GraphQL.Client;

namespace Trax.Api.Tests.GraphQLClient.UnitTests;

/// <summary>
/// IntrospectionSdlBuilder converts the introspection-response POCO model into SDL that
/// graphql-dotnet's <c>Schema.For</c> can parse. Each branch in <c>WriteType</c> handles a
/// different GraphQL kind, and each has at least one production schema in the wild that
/// uses it. If a branch breaks, validation against the relevant servers stops working
/// silently - the introspection succeeds but the rebuilt schema is missing types.
///
/// These tests construct introspection POCOs directly (bypassing the HTTP fetch) and
/// assert specific tokens in the resulting SDL. Token-level assertions catch real regressions:
/// a UNION branch that emits <c>type</c> instead of <c>union</c> would produce SDL that
/// parses but represents a different schema.
/// </summary>
[TestFixture]
public class IntrospectionSdlBuilderTests
{
    private static IntrospectionSchema MinimalSchema(params IntrospectionType[] extra)
    {
        var schema = new IntrospectionSchema
        {
            QueryType = new IntrospectionTypeRef { Kind = "OBJECT", Name = "Query" },
            Types = new List<IntrospectionType>
            {
                new()
                {
                    Kind = "OBJECT",
                    Name = "Query",
                    Fields = new List<IntrospectionField>
                    {
                        new()
                        {
                            Name = "hello",
                            Type = new IntrospectionTypeRef { Kind = "SCALAR", Name = "String" },
                        },
                    },
                },
            },
        };
        foreach (var t in extra)
            schema.Types.Add(t);
        return schema;
    }

    [Test]
    public void Build_EmitsSchemaBlock_WithQueryRoot()
    {
        var sdl = IntrospectionSdlBuilder.Build(MinimalSchema());

        sdl.Should().Contain("schema {");
        sdl.Should().Contain("query: Query");
        sdl.Should().Contain("type Query");
    }

    [Test]
    public void Build_EmitsMutationRootWhenPresent()
    {
        var schema = MinimalSchema(
            new IntrospectionType
            {
                Kind = "OBJECT",
                Name = "Mutation",
                Fields = new List<IntrospectionField>
                {
                    new()
                    {
                        Name = "noop",
                        Type = new IntrospectionTypeRef { Kind = "SCALAR", Name = "Boolean" },
                    },
                },
            }
        );
        schema.MutationType = new IntrospectionTypeRef { Kind = "OBJECT", Name = "Mutation" };

        var sdl = IntrospectionSdlBuilder.Build(schema);

        sdl.Should().Contain("mutation: Mutation");
        sdl.Should().Contain("type Mutation");
    }

    [Test]
    public void Build_EmitsSubscriptionRootWhenPresent()
    {
        var schema = MinimalSchema(
            new IntrospectionType
            {
                Kind = "OBJECT",
                Name = "Subscription",
                Fields = new List<IntrospectionField>
                {
                    new()
                    {
                        Name = "ticks",
                        Type = new IntrospectionTypeRef { Kind = "SCALAR", Name = "Int" },
                    },
                },
            }
        );
        schema.SubscriptionType = new IntrospectionTypeRef
        {
            Kind = "OBJECT",
            Name = "Subscription",
        };

        var sdl = IntrospectionSdlBuilder.Build(schema);

        sdl.Should().Contain("subscription: Subscription");
    }

    [Test]
    public void Build_SkipsIntrospectionMetaTypes()
    {
        // Real introspection responses include __Schema, __Type, etc. Including them in
        // the rebuilt SDL would shadow graphql-dotnet's built-ins and break validation.
        var schema = MinimalSchema(
            new IntrospectionType { Kind = "OBJECT", Name = "__Schema" },
            new IntrospectionType { Kind = "OBJECT", Name = "__Type" }
        );

        var sdl = IntrospectionSdlBuilder.Build(schema);

        sdl.Should().NotContain("__Schema");
        sdl.Should().NotContain("__Type");
    }

    [Test]
    public void Build_EmitsUnionWithPossibleTypes()
    {
        var schema = MinimalSchema(
            new IntrospectionType
            {
                Kind = "UNION",
                Name = "SearchResult",
                PossibleTypes = new List<IntrospectionTypeRef>
                {
                    new() { Kind = "OBJECT", Name = "Player" },
                    new() { Kind = "OBJECT", Name = "Guild" },
                },
            }
        );

        var sdl = IntrospectionSdlBuilder.Build(schema);

        sdl.Should().Contain("union SearchResult = Player | Guild");
    }

    [Test]
    public void Build_EmitsInterfaceImplementations()
    {
        var schema = MinimalSchema(
            new IntrospectionType
            {
                Kind = "INTERFACE",
                Name = "Node",
                Fields = new List<IntrospectionField>
                {
                    new()
                    {
                        Name = "id",
                        Type = new IntrospectionTypeRef { Kind = "SCALAR", Name = "ID" },
                    },
                },
            },
            new IntrospectionType
            {
                Kind = "OBJECT",
                Name = "Player",
                Interfaces = new List<IntrospectionTypeRef>
                {
                    new() { Kind = "INTERFACE", Name = "Node" },
                },
                Fields = new List<IntrospectionField>
                {
                    new()
                    {
                        Name = "id",
                        Type = new IntrospectionTypeRef { Kind = "SCALAR", Name = "ID" },
                    },
                    new()
                    {
                        Name = "name",
                        Type = new IntrospectionTypeRef { Kind = "SCALAR", Name = "String" },
                    },
                },
            }
        );

        var sdl = IntrospectionSdlBuilder.Build(schema);

        sdl.Should().Contain("interface Node");
        sdl.Should().Contain("type Player implements Node");
    }

    [Test]
    public void Build_EmitsEnumWithDeprecation()
    {
        var schema = MinimalSchema(
            new IntrospectionType
            {
                Kind = "ENUM",
                Name = "Rank",
                EnumValues = new List<IntrospectionEnumValue>
                {
                    new() { Name = "BRONZE" },
                    new()
                    {
                        Name = "WOOD",
                        IsDeprecated = true,
                        DeprecationReason = "Use BRONZE",
                    },
                },
            }
        );

        var sdl = IntrospectionSdlBuilder.Build(schema);

        sdl.Should().Contain("enum Rank {");
        sdl.Should().Contain("BRONZE");
        sdl.Should().Contain("WOOD @deprecated(reason: \"Use BRONZE\")");
    }

    [Test]
    public void Build_EmitsInputObjectWithDefaultValue()
    {
        var schema = MinimalSchema(
            new IntrospectionType
            {
                Kind = "INPUT_OBJECT",
                Name = "PageInput",
                InputFields = new List<IntrospectionInputValue>
                {
                    new()
                    {
                        Name = "limit",
                        Type = new IntrospectionTypeRef { Kind = "SCALAR", Name = "Int" },
                        DefaultValue = "10",
                    },
                    new()
                    {
                        Name = "offset",
                        Type = new IntrospectionTypeRef
                        {
                            Kind = "NON_NULL",
                            OfType = new IntrospectionTypeRef { Kind = "SCALAR", Name = "Int" },
                        },
                    },
                },
            }
        );

        var sdl = IntrospectionSdlBuilder.Build(schema);

        sdl.Should().Contain("input PageInput");
        sdl.Should().Contain("limit: Int = 10");
        sdl.Should().Contain("offset: Int!");
    }

    [Test]
    public void Build_EmitsFieldArgumentsWithDefaults()
    {
        // Field-level arguments with defaults are a separate code path from input objects.
        var schema = MinimalSchema(
            new IntrospectionType
            {
                Kind = "OBJECT",
                Name = "WithArgs",
                Fields = new List<IntrospectionField>
                {
                    new()
                    {
                        Name = "search",
                        Type = new IntrospectionTypeRef { Kind = "SCALAR", Name = "String" },
                        Args = new List<IntrospectionInputValue>
                        {
                            new()
                            {
                                Name = "q",
                                Type = new IntrospectionTypeRef
                                {
                                    Kind = "NON_NULL",
                                    OfType = new IntrospectionTypeRef
                                    {
                                        Kind = "SCALAR",
                                        Name = "String",
                                    },
                                },
                            },
                            new()
                            {
                                Name = "limit",
                                Type = new IntrospectionTypeRef { Kind = "SCALAR", Name = "Int" },
                                DefaultValue = "20",
                            },
                        },
                    },
                },
            }
        );

        var sdl = IntrospectionSdlBuilder.Build(schema);

        sdl.Should().Contain("search(q: String!, limit: Int = 20): String");
    }

    [Test]
    public void Build_EmitsDeprecatedFieldWithReason()
    {
        var schema = MinimalSchema(
            new IntrospectionType
            {
                Kind = "OBJECT",
                Name = "Stale",
                Fields = new List<IntrospectionField>
                {
                    new()
                    {
                        Name = "old",
                        Type = new IntrospectionTypeRef { Kind = "SCALAR", Name = "String" },
                        IsDeprecated = true,
                        DeprecationReason = "Use new",
                    },
                },
            }
        );

        var sdl = IntrospectionSdlBuilder.Build(schema);

        sdl.Should().Contain("old: String @deprecated(reason: \"Use new\")");
    }

    [Test]
    public void Build_WriteTypeRef_HandlesNestedListsAndNonNulls()
    {
        // [String!]! - the trickiest GraphQL type ref shape, doubly wrapped.
        var schema = MinimalSchema(
            new IntrospectionType
            {
                Kind = "OBJECT",
                Name = "Nested",
                Fields = new List<IntrospectionField>
                {
                    new()
                    {
                        Name = "tags",
                        Type = new IntrospectionTypeRef
                        {
                            Kind = "NON_NULL",
                            OfType = new IntrospectionTypeRef
                            {
                                Kind = "LIST",
                                OfType = new IntrospectionTypeRef
                                {
                                    Kind = "NON_NULL",
                                    OfType = new IntrospectionTypeRef
                                    {
                                        Kind = "SCALAR",
                                        Name = "String",
                                    },
                                },
                            },
                        },
                    },
                },
            }
        );

        var sdl = IntrospectionSdlBuilder.Build(schema);

        sdl.Should().Contain("tags: [String!]!");
    }

    [Test]
    public void Build_NonNullMissingOfType_Throws()
    {
        var schema = MinimalSchema(
            new IntrospectionType
            {
                Kind = "OBJECT",
                Name = "Broken",
                Fields = new List<IntrospectionField>
                {
                    new()
                    {
                        Name = "bad",
                        // NON_NULL with no OfType is malformed; the builder should surface it.
                        Type = new IntrospectionTypeRef { Kind = "NON_NULL", OfType = null },
                    },
                },
            }
        );

        var act = () => IntrospectionSdlBuilder.Build(schema);

        act.Should().Throw<GraphQLSchemaIntrospectionException>().WithMessage("*NON_NULL*");
    }

    [Test]
    public void Build_ListMissingOfType_Throws()
    {
        var schema = MinimalSchema(
            new IntrospectionType
            {
                Kind = "OBJECT",
                Name = "Broken",
                Fields = new List<IntrospectionField>
                {
                    new()
                    {
                        Name = "bad",
                        Type = new IntrospectionTypeRef { Kind = "LIST", OfType = null },
                    },
                },
            }
        );

        var act = () => IntrospectionSdlBuilder.Build(schema);

        act.Should().Throw<GraphQLSchemaIntrospectionException>().WithMessage("*LIST*");
    }

    [Test]
    public void Build_NamedTypeRefMissingName_Throws()
    {
        // A SCALAR/OBJECT/INTERFACE/UNION/ENUM/INPUT_OBJECT ref must have a name.
        var schema = MinimalSchema(
            new IntrospectionType
            {
                Kind = "OBJECT",
                Name = "Broken",
                Fields = new List<IntrospectionField>
                {
                    new()
                    {
                        Name = "bad",
                        Type = new IntrospectionTypeRef { Kind = "OBJECT", Name = null },
                    },
                },
            }
        );

        var act = () => IntrospectionSdlBuilder.Build(schema);

        act.Should().Throw<GraphQLSchemaIntrospectionException>().WithMessage("*missing name*");
    }

    [Test]
    public void Build_UnknownTypeKind_Throws()
    {
        // Defensive: the introspection spec defines a closed set of kinds. Anything else
        // is a server bug or a future GraphQL feature we don't handle, and we'd rather fail
        // loudly than silently emit half-correct SDL.
        var schema = MinimalSchema(
            new IntrospectionType { Kind = "SOMETHING_NEW", Name = "Future" }
        );

        var act = () => IntrospectionSdlBuilder.Build(schema);

        act.Should().Throw<GraphQLSchemaIntrospectionException>().WithMessage("*SOMETHING_NEW*");
    }

    [Test]
    public void Build_DeprecationReasonWithSpecialChars_IsEscaped()
    {
        var schema = MinimalSchema(
            new IntrospectionType
            {
                Kind = "OBJECT",
                Name = "WithDeprecation",
                Fields = new List<IntrospectionField>
                {
                    new()
                    {
                        Name = "old",
                        Type = new IntrospectionTypeRef { Kind = "SCALAR", Name = "String" },
                        IsDeprecated = true,
                        DeprecationReason = "Quoted \"reason\" with\nnewline\tand\\backslash",
                    },
                },
            }
        );

        var sdl = IntrospectionSdlBuilder.Build(schema);

        // Real regressions: a missing backslash escape would make the SDL parser see an
        // unterminated string; a missing newline escape would break the SDL across lines.
        sdl.Should().Contain("\\\"reason\\\"");
        sdl.Should().Contain("\\n");
        sdl.Should().Contain("\\t");
        sdl.Should().Contain("\\\\backslash");
    }

    [Test]
    public void Build_DeprecatedWithoutReason_EmitsBareDeprecatedDirective()
    {
        var schema = MinimalSchema(
            new IntrospectionType
            {
                Kind = "OBJECT",
                Name = "BareDep",
                Fields = new List<IntrospectionField>
                {
                    new()
                    {
                        Name = "old",
                        Type = new IntrospectionTypeRef { Kind = "SCALAR", Name = "String" },
                        IsDeprecated = true,
                        DeprecationReason = null,
                    },
                },
            }
        );

        var sdl = IntrospectionSdlBuilder.Build(schema);

        sdl.Should().Contain("@deprecated");
        sdl.Should().NotContain("@deprecated(");
    }

    [Test]
    public void Build_EmitsScalarType()
    {
        var schema = MinimalSchema(new IntrospectionType { Kind = "SCALAR", Name = "DateTime" });

        var sdl = IntrospectionSdlBuilder.Build(schema);

        sdl.Should().Contain("scalar DateTime");
    }

    [Test]
    public void Build_TypeWithNullName_IsSkipped()
    {
        var schema = MinimalSchema(new IntrospectionType { Kind = "OBJECT", Name = null });

        var sdl = IntrospectionSdlBuilder.Build(schema);

        // Doesn't throw, just skips - covers a defensive branch in the foreach loop.
        sdl.Should().Contain("type Query");
    }
}
