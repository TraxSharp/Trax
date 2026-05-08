using FluentAssertions;
using Trax.Api.GraphQL.PersistedOperations.Storage;

namespace Trax.Api.Tests.PersistedOperations.UnitTests;

[TestFixture]
public class PersistedOperationIdParserTests
{
    [TestCase("userProfile_v1", "userProfile", 1)]
    [TestCase("Echo_v42", "Echo", 42)]
    [TestCase("_camel_v0", "_camel", 0)]
    [TestCase("with_underscore_5_v999", "with_underscore_5", 999)]
    public void Parse_ValidId_ReturnsNameAndVersion(string id, string name, int version)
    {
        var parsed = PersistedOperationIdParser.Parse(id);
        parsed.Name.Should().Be(name);
        parsed.Version.Should().Be(version);
    }

    [TestCase("userProfile")]
    [TestCase("userProfile_")]
    [TestCase("userProfile_v")]
    [TestCase("userProfile_va")]
    [TestCase("_v1")]
    [TestCase("1userProfile_v1")]
    [TestCase("user-profile_v1")]
    [TestCase("userProfile_V1")]
    [TestCase("userProfile_v-1")]
    [TestCase("userProfile.v1")]
    public void Parse_InvalidId_Throws(string id)
    {
        Action act = () => PersistedOperationIdParser.Parse(id);
        act.Should().Throw<FormatException>().WithMessage("*name_vN*");
    }

    [Test]
    public void Parse_NullId_Throws()
    {
        Action act = () => PersistedOperationIdParser.Parse(null!);
        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void Parse_EmptyId_Throws()
    {
        Action act = () => PersistedOperationIdParser.Parse(string.Empty);
        act.Should().Throw<ArgumentException>();
    }

    [TestCase("userProfile_v1", true)]
    [TestCase("userProfile", false)]
    [TestCase("userProfile.v1", false)]
    [TestCase("", false)]
    public void IsValid_ReportsCorrectly(string id, bool expected) =>
        PersistedOperationIdParser.IsValid(id).Should().Be(expected);

    [Test]
    public void IsValid_Null_ReturnsFalse() =>
        PersistedOperationIdParser.IsValid(null!).Should().BeFalse();
}
