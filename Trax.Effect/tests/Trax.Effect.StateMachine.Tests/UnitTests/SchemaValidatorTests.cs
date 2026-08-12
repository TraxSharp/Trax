using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using FluentAssertions;

namespace Trax.Effect.StateMachine.Tests.UnitTests;

/// <summary>
/// The schema-derived validator: every non-nullable field present and correctly typed, no fields outside the
/// schema, and attribute-derived constraints satisfied. This is what replaces a hand-written <c>Holds(...)</c>,
/// so its accept/reject decision (the contract) is pinned; the message text is not.
/// </summary>
public class SchemaValidatorTests
{
    private sealed record Ctx
    {
        [MinLength(1)]
        public string Name { get; init; } = "";
        public string? Note { get; init; }
        public int Count { get; init; }
    }

    private static readonly ContextSchema Schema = SchemaReflection.For<Ctx>();

    private static bool Valid(JsonObject context) =>
        SchemaValidator.Validate(Schema, context) is null;

    [Test]
    public void A_context_matching_the_schema_is_valid()
    {
        Valid(new JsonObject { ["name"] = "a", ["count"] = 1 }).Should().BeTrue();
        Valid(
                new JsonObject
                {
                    ["name"] = "a",
                    ["note"] = "hi",
                    ["count"] = 1,
                }
            )
            .Should()
            .BeTrue();
        Valid(
                new JsonObject
                {
                    ["name"] = "a",
                    ["note"] = null,
                    ["count"] = 1,
                }
            )
            .Should()
            .BeTrue();
    }

    [Test]
    public void A_missing_non_nullable_field_is_invalid()
    {
        Valid(new JsonObject { ["count"] = 1 }).Should().BeFalse("name is required");
        Valid(new JsonObject { ["name"] = "a" }).Should().BeFalse("count is required");
    }

    [Test]
    public void A_wrong_typed_field_is_invalid()
    {
        Valid(new JsonObject { ["name"] = 5, ["count"] = 1 }).Should().BeFalse();
        Valid(new JsonObject { ["name"] = "a", ["count"] = "x" }).Should().BeFalse();
        Valid(
                new JsonObject
                {
                    ["name"] = new JsonObject { ["x"] = 1 },
                    ["count"] = 1,
                }
            )
            .Should()
            .BeFalse("an object is not a string");
    }

    [Test]
    public void A_failed_attribute_constraint_is_invalid()
    {
        Valid(new JsonObject { ["name"] = "", ["count"] = 1 })
            .Should()
            .BeFalse("[MinLength(1)] forbids empty");
    }

    [Test]
    public void A_field_outside_the_schema_is_invalid()
    {
        Valid(
                new JsonObject
                {
                    ["name"] = "a",
                    ["count"] = 1,
                    ["extra"] = true,
                }
            )
            .Should()
            .BeFalse();
    }

    [Test]
    public void An_empty_schema_accepts_only_an_empty_context()
    {
        SchemaValidator.Validate(ContextSchema.Empty, new JsonObject()).Should().BeNull();
        SchemaValidator
            .Validate(ContextSchema.Empty, new JsonObject { ["x"] = 1 })
            .Should()
            .NotBeNull();
    }

    private sealed record Shapes
    {
        public string[] Items { get; init; } = [];
        public bool Flag { get; init; }
        public string? Tag { get; init; }
    }

    [Test]
    public void Validates_array_boolean_and_nullable_field_types()
    {
        var schema = SchemaReflection.For<Shapes>();
        bool Ok(JsonObject c) => SchemaValidator.Validate(schema, c) is null;

        // Correct types (tag optional).
        Ok(new JsonObject { ["items"] = new JsonArray("a"), ["flag"] = true }).Should().BeTrue();
        // Array field given a non-array, and boolean field given a non-boolean.
        Ok(new JsonObject { ["items"] = "x", ["flag"] = true }).Should().BeFalse();
        Ok(new JsonObject { ["items"] = new JsonArray(), ["flag"] = 1 }).Should().BeFalse();
        // Nullable field present with the wrong type vs the right type.
        Ok(
                new JsonObject
                {
                    ["items"] = new JsonArray(),
                    ["flag"] = true,
                    ["tag"] = 5,
                }
            )
            .Should()
            .BeFalse();
        Ok(
                new JsonObject
                {
                    ["items"] = new JsonArray(),
                    ["flag"] = true,
                    ["tag"] = "ok",
                }
            )
            .Should()
            .BeTrue();
    }

    private sealed record Nested
    {
        public int A { get; init; }
    }

    private sealed record WithObject
    {
        public Nested Meta { get; init; } = new();
    }

    [Test]
    public void Validates_an_object_typed_field()
    {
        // A field reflected to JsonFieldType.Object accepts a JSON object and rejects a scalar or array.
        var schema = SchemaReflection.For<WithObject>();
        bool Ok(JsonObject c) => SchemaValidator.Validate(schema, c) is null;

        Ok(new JsonObject { ["meta"] = new JsonObject { ["a"] = 1 } }).Should().BeTrue();
        Ok(new JsonObject { ["meta"] = "x" }).Should().BeFalse("a string is not an object");
        Ok(new JsonObject { ["meta"] = new JsonArray() })
            .Should()
            .BeFalse("an array is not an object");
    }
}
