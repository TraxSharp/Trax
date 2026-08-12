using System.ComponentModel.DataAnnotations;
using FluentAssertions;

namespace Trax.Effect.StateMachine.Tests.UnitTests;

/// <summary>
/// The typed authoring layer: a member expression resolves to the JSON field name it maps to, and a context
/// record reflects to a <see cref="ContextSchema"/>. Together these are what remove structural field-name
/// strings from the authoring surface while keeping the stored rule and the IR as plain data.
/// </summary>
public class TypedSchemaTests
{
    private sealed record ReviewContext
    {
        public string[] Items { get; init; } = [];
        public string? Receipt { get; init; }
        public int Total { get; init; }
        public bool Gift { get; init; }
    }

    #region MemberPath

    [Test]
    public void MemberPath_maps_a_reference_property_to_a_camelCase_json_key()
    {
        MemberPath.Of((ReviewContext c) => c.Receipt).Should().Be("receipt");
        MemberPath.Of((ReviewContext c) => c.Items).Should().Be("items");
    }

    [Test]
    public void MemberPath_handles_value_typed_properties_wrapped_in_a_convert()
    {
        // int/bool selectors compile to a Convert(...) node; the resolver must see through it.
        MemberPath.Of((ReviewContext c) => c.Total).Should().Be("total");
        MemberPath.Of((ReviewContext c) => c.Gift).Should().Be("gift");
    }

    [Test]
    public void MemberPath_rejects_a_non_member_selector()
    {
        var act = () => MemberPath.Of((ReviewContext _) => "literal");
        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void ToJsonName_lowercases_the_first_letter_and_leaves_the_rest()
    {
        MemberPath.ToJsonName("PaidWith").Should().Be("paidWith");
        MemberPath.ToJsonName("X").Should().Be("x");
        MemberPath.ToJsonName("already").Should().Be("already");
        MemberPath.ToJsonName("").Should().Be("");
    }

    #endregion

    #region SchemaReflection

    [Test]
    public void SchemaReflection_reads_names_types_and_nullability_from_the_record()
    {
        var schema = SchemaReflection.For<ReviewContext>();

        // Sorted by key (ordinal), matching the canonical wire.
        schema.Fields.Select(f => f.Name).Should().Equal("gift", "items", "receipt", "total");

        schema.Fields.Single(f => f.Name == "items").Type.Should().Be(JsonFieldType.Array);
        schema.Fields.Single(f => f.Name == "receipt").Type.Should().Be(JsonFieldType.String);
        schema.Fields.Single(f => f.Name == "total").Type.Should().Be(JsonFieldType.Number);
        schema.Fields.Single(f => f.Name == "gift").Type.Should().Be(JsonFieldType.Boolean);

        // string? is nullable; string[]/int/bool are not.
        schema.Fields.Single(f => f.Name == "receipt").Nullable.Should().BeTrue();
        schema.Fields.Single(f => f.Name == "items").Nullable.Should().BeFalse();
        schema.Fields.Single(f => f.Name == "total").Nullable.Should().BeFalse();
    }

    [Test]
    public void SchemaReflection_of_an_empty_record_is_an_empty_schema()
    {
        SchemaReflection.For<EmptyContext>().Fields.Should().BeEmpty();
    }

    private sealed record EmptyContext;

    private sealed record NestedMeta
    {
        public int A { get; init; }
    }

    private sealed record WithObjectAndNullableValue
    {
        public NestedMeta Meta { get; init; } = new();
        public int? Limit { get; init; }
    }

    [Test]
    public void SchemaReflection_maps_a_complex_property_to_object_and_a_nullable_value_type_to_nullable()
    {
        var schema = SchemaReflection.For<WithObjectAndNullableValue>();

        var meta = schema.Fields.Single(f => f.Name == "meta");
        meta.Type.Should().Be(JsonFieldType.Object, "a non-enumerable complex type maps to object");
        meta.Nullable.Should().BeFalse();

        var limit = schema.Fields.Single(f => f.Name == "limit");
        limit.Type.Should().Be(JsonFieldType.Number);
        limit.Nullable.Should().BeTrue("int? is a nullable value type");
    }

    private sealed record TypedCollections
    {
        public int[] Ids { get; init; } = [];
        public List<string> Tags { get; init; } = [];
        public bool[] Flags { get; init; } = [];
        public object[] Things { get; init; } = [];
        public System.Collections.ArrayList Raw { get; init; } = [];

        [AllowedValues("federal", "state", "unsure")]
        public string Legislature { get; init; } = "federal";
    }

    [Test]
    public void SchemaReflection_derives_array_element_and_allowed_value_constraints()
    {
        var schema = SchemaReflection.For<TypedCollections>();
        Rule.ArrayOf? ArrayConstraint(string name) =>
            schema
                .Fields.Single(f => f.Name == name)
                .Constraints.OfType<Rule.ArrayOf>()
                .SingleOrDefault();

        ArrayConstraint("ids")
            .Should()
            .Be(new Rule.ArrayOf(RuleSource.Context, "ids", JsonFieldType.Number));
        ArrayConstraint("tags")
            .Should()
            .Be(new Rule.ArrayOf(RuleSource.Context, "tags", JsonFieldType.String));
        ArrayConstraint("flags")
            .Should()
            .Be(new Rule.ArrayOf(RuleSource.Context, "flags", JsonFieldType.Boolean));

        // Element types that are not string/bool/number get no ArrayOf: an array of objects, and a
        // non-generic collection whose element type can't be read.
        ArrayConstraint("things")
            .Should()
            .BeNull("an array of objects has no element constraint");
        ArrayConstraint("raw")
            .Should()
            .BeNull("a non-generic collection has no element constraint");

        var legislature = schema
            .Fields.Single(f => f.Name == "legislature")
            .Constraints.OfType<Rule.OneOf>()
            .Single();
        legislature.Values.Should().Equal("federal", "state", "unsure");
    }

    #endregion
}
