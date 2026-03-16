using FluentAssertions;
using Trax.Cli.Schema;

namespace Trax.Cli.Tests.UnitTests;

[TestFixture]
public class SchemaDetectorTests
{
    #region DetectByExtension

    [Test]
    public void Detect_GraphqlExtension_ReturnsGraphql()
    {
        SchemaDetector.Detect("schema.graphql").Should().Be("graphql");
    }

    [Test]
    public void Detect_GqlExtension_ReturnsGraphql()
    {
        SchemaDetector.Detect("schema.gql").Should().Be("graphql");
    }

    [Test]
    public void Detect_JsonExtension_ReturnsOpenapi()
    {
        SchemaDetector.Detect("spec.json").Should().Be("openapi");
    }

    [Test]
    public void Detect_YamlExtension_ReturnsOpenapi()
    {
        SchemaDetector.Detect("spec.yaml").Should().Be("openapi");
    }

    [Test]
    public void Detect_YmlExtension_ReturnsOpenapi()
    {
        SchemaDetector.Detect("spec.yml").Should().Be("openapi");
    }

    [Test]
    public void Detect_TxtExtension_ThrowsArgumentException()
    {
        var act = () => SchemaDetector.Detect("file.txt");
        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region ExplicitTypeOverride

    [Test]
    public void Detect_ExplicitGraphql_ReturnsGraphql()
    {
        SchemaDetector.Detect("anything.xyz", "graphql").Should().Be("graphql");
    }

    [Test]
    public void Detect_ExplicitOpenapi_ReturnsOpenapi()
    {
        SchemaDetector.Detect("anything.xyz", "openapi").Should().Be("openapi");
    }

    [Test]
    public void Detect_ExplicitInvalid_ThrowsArgumentException()
    {
        var act = () => SchemaDetector.Detect("anything.xyz", "invalid");
        act.Should().Throw<ArgumentException>();
    }

    #endregion
}
