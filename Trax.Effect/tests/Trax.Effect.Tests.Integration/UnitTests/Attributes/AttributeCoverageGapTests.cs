using System.Text.Json;
using FluentAssertions;
using NUnit.Framework;
using Trax.Effect.Attributes;
using Trax.Effect.Utils;

namespace Trax.Effect.Tests.Integration.UnitTests.Attributes;

[TestFixture]
public class AttributeCoverageGapTests
{
    #region TraxAuthorizeAttribute

    [Test]
    public void TraxAuthorize_DefaultCtor_NoPolicyOrRoles()
    {
        var attr = new TraxAuthorizeAttribute();
        attr.Policy.Should().BeNull();
        attr.Roles.Should().BeNull();
    }

    [Test]
    public void TraxAuthorize_PolicyCtor_SetsPolicy()
    {
        var attr = new TraxAuthorizeAttribute("Admin");
        attr.Policy.Should().Be("Admin");
        attr.Roles.Should().BeNull();
    }

    [Test]
    public void TraxAuthorize_InitProperties_AreSet()
    {
        var attr = new TraxAuthorizeAttribute { Roles = "admin,viewer" };
        attr.Roles.Should().Be("admin,viewer");
        attr.Policy.Should().BeNull();
    }

    #endregion

    #region TraxConcurrencyLimitAttribute

    [Test]
    public void TraxConcurrencyLimit_ValidLimit_SetsValue()
    {
        var attr = new TraxConcurrencyLimitAttribute(5);
        attr.MaxConcurrent.Should().Be(5);
    }

    [Test]
    public void TraxConcurrencyLimit_LimitOfOne_IsAllowed()
    {
        var attr = new TraxConcurrencyLimitAttribute(1);
        attr.MaxConcurrent.Should().Be(1);
    }

    [Test]
    public void TraxConcurrencyLimit_Zero_Throws()
    {
        Action act = () => _ = new TraxConcurrencyLimitAttribute(0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void TraxConcurrencyLimit_Negative_Throws()
    {
        Action act = () => _ = new TraxConcurrencyLimitAttribute(-3);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    #endregion

    #region TraxQueryAttribute

    [Test]
    public void TraxQuery_DefaultCtor_NullProperties()
    {
        var attr = new TraxQueryAttribute();
        attr.Name.Should().BeNull();
        attr.Description.Should().BeNull();
        attr.DeprecationReason.Should().BeNull();
        attr.Namespace.Should().BeNull();
    }

    [Test]
    public void TraxQuery_InitProperties_AreSet()
    {
        var attr = new TraxQueryAttribute
        {
            Name = "lookup",
            Description = "looks up a thing",
            DeprecationReason = "use lookupV2",
            Namespace = "discovery",
        };
        attr.Name.Should().Be("lookup");
        attr.Description.Should().Be("looks up a thing");
        attr.DeprecationReason.Should().Be("use lookupV2");
        attr.Namespace.Should().Be("discovery");
    }

    #endregion

    #region TraxQueryModelAttribute

    [Test]
    public void TraxQueryModel_DefaultCtor_HasExpectedDefaults()
    {
        var attr = new TraxQueryModelAttribute();
        attr.Name.Should().BeNull();
        attr.Description.Should().BeNull();
        attr.DeprecationReason.Should().BeNull();
        attr.Paging.Should().BeTrue();
        attr.Filtering.Should().BeTrue();
        attr.Sorting.Should().BeTrue();
        attr.Projection.Should().BeTrue();
        attr.BindFields.Should().Be(FieldBindingBehavior.Implicit);
        attr.Namespace.Should().BeNull();
    }

    [Test]
    public void TraxQueryModel_InitProperties_OverrideDefaults()
    {
        var attr = new TraxQueryModelAttribute
        {
            Name = "users",
            Description = "the users",
            DeprecationReason = "use usersV2",
            Paging = false,
            Filtering = false,
            Sorting = false,
            Projection = false,
            BindFields = FieldBindingBehavior.Explicit,
            Namespace = "admin",
        };
        attr.Name.Should().Be("users");
        attr.Description.Should().Be("the users");
        attr.DeprecationReason.Should().Be("use usersV2");
        attr.Paging.Should().BeFalse();
        attr.Filtering.Should().BeFalse();
        attr.Sorting.Should().BeFalse();
        attr.Projection.Should().BeFalse();
        attr.BindFields.Should().Be(FieldBindingBehavior.Explicit);
        attr.Namespace.Should().Be("admin");
    }

    #endregion

    #region ValueTupleConverter

    [Test]
    public void ValueTupleConverter_Serialize_WritesArray()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new ValueTupleConverter());

        var tuple = (1, "hello", 3);
        var json = JsonSerializer.Serialize(tuple, options);

        json.Should().Contain("1");
        json.Should().Contain("hello");
        json.Should().StartWith("[");
        json.Should().EndWith("]");
    }

    [Test]
    public void ValueTupleConverter_CanConvert_RejectsNonValueTuple()
    {
        var converter = new ValueTupleConverter();

        converter.CanConvert(typeof(string)).Should().BeFalse();
        converter.CanConvert(typeof(int)).Should().BeFalse();
        converter.CanConvert(typeof(List<int>)).Should().BeFalse();
        converter.CanConvert(typeof((int, string))).Should().BeTrue();
        converter.CanConvert(typeof((int, int, int, int))).Should().BeTrue();
    }

    [Test]
    public void ValueTupleConverter_WrongLengthArray_Throws()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new ValueTupleConverter());

        Action act = () => JsonSerializer.Deserialize<(int, string)>("[1]", options);
        act.Should().Throw<JsonException>();
    }

    #endregion
}
