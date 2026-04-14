using System.Text.Json;
using FluentAssertions;
using NUnit.Framework;
using Trax.Samples.JobHunt.E2E.Fixtures;

namespace Trax.Samples.JobHunt.E2E;

[TestFixture]
public class SchemaTests : JobHuntApiTestFixture
{
    [Test]
    public async Task Introspection_ReturnsValidSchema()
    {
        var result = await GraphQL.SendAsync(
            """
            {
              __schema {
                queryType { name }
                mutationType { name }
                subscriptionType { name }
              }
            }
            """
        );

        result.HasErrors.Should().BeFalse();
        var schema = result.GetData("__schema");
        schema.GetProperty("queryType").GetProperty("name").GetString().Should().NotBeNullOrEmpty();
        schema
            .GetProperty("mutationType")
            .GetProperty("name")
            .GetString()
            .Should()
            .NotBeNullOrEmpty();
    }

    [Test]
    public async Task Schema_HasQueryAndMutationFields()
    {
        // Trax.Api.GraphQL exposes query and mutation types. Phase 0 has no trains
        // registered, so only framework-level fields (operations, deadLetters, etc.)
        // are present. We assert the schema is non-empty, confirming Trax wired correctly.
        var result = await GraphQL.SendAsync(
            """
            {
              __schema {
                queryType {
                  fields { name }
                }
                mutationType {
                  fields { name }
                }
              }
            }
            """
        );

        result.HasErrors.Should().BeFalse();

        var queryFields = GetFieldNames(result.GetData("__schema", "queryType", "fields"));
        var mutationFields = GetFieldNames(result.GetData("__schema", "mutationType", "fields"));

        queryFields.Should().NotBeEmpty();
        mutationFields.Should().NotBeEmpty();
    }

    private static List<string> GetFieldNames(JsonElement fields)
    {
        var names = new List<string>();
        foreach (var field in fields.EnumerateArray())
            names.Add(field.GetProperty("name").GetString()!);
        return names;
    }
}
