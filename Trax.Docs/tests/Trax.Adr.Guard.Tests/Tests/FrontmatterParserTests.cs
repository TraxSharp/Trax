namespace Trax.Adr.Guard.Tests.Tests;

[TestFixture]
public class FrontmatterParserTests
{
    private static Frontmatter Parse(params string[] blockLines) =>
        Frontmatter.Parse("---\n" + string.Join('\n', blockLines) + "\n---\n\n# Title\n");

    #region Accepted shapes

    [Test]
    public void Parse_ScalarValue_IsReadAsScalar()
    {
        var frontmatter = Parse("status: accepted");

        frontmatter.Parsed.Should().BeTrue(frontmatter.Error);
        frontmatter.Scalar("status").Should().Be("accepted");
        frontmatter.List("status").Should().BeNull("a scalar is not silently coerced into a list");
    }

    [Test]
    public void Parse_FlowList_IsReadAsList()
    {
        var frontmatter = Parse("areas: [migrations, data-model]");

        frontmatter.Parsed.Should().BeTrue(frontmatter.Error);
        frontmatter.List("areas").Should().Equal("migrations", "data-model");
    }

    [Test]
    public void Parse_BlockList_IsReadAsList()
    {
        var frontmatter = Parse("areas:", "  - migrations", "  - data-model");

        frontmatter.Parsed.Should().BeTrue(frontmatter.Error);
        frontmatter.List("areas").Should().Equal("migrations", "data-model");
    }

    [Test]
    public void Parse_EmptyFlowList_IsAnEmptyList_NotAMissingKey()
    {
        var frontmatter = Parse("areas: []");

        frontmatter.Parsed.Should().BeTrue(frontmatter.Error);
        frontmatter.Has("areas").Should().BeTrue();
        frontmatter.List("areas").Should().BeEmpty();
    }

    [Test]
    public void Parse_QuotedValues_AreUnquoted()
    {
        var frontmatter = Parse("status: \"accepted\"", "areas: ['migrations']");

        frontmatter.Scalar("status").Should().Be("accepted");
        frontmatter.List("areas").Should().Equal("migrations");
    }

    [Test]
    public void Parse_Comment_IsIgnored()
    {
        var frontmatter = Parse("# a comment", "status: accepted");

        frontmatter.Parsed.Should().BeTrue(frontmatter.Error);
        frontmatter.Keys.Should().Equal("status");
    }

    [Test]
    public void Parse_KeysAreReportedInOrder()
    {
        var frontmatter = Parse("authors: [a]", "areas: [testing]", "status: accepted");

        frontmatter.Keys.Should().Equal("authors", "areas", "status");
    }

    #endregion

    #region Rejected shapes

    [Test]
    public void Parse_NoOpeningDelimiter_IsRejected()
    {
        var frontmatter = Frontmatter.Parse("# Title\n\nNo frontmatter here.\n");

        frontmatter.Parsed.Should().BeFalse();
        frontmatter.Error.Should().Contain("must open with a '---' line");
    }

    [Test]
    public void Parse_UnclosedBlock_IsRejected()
    {
        var frontmatter = Frontmatter.Parse("---\nstatus: accepted\n");

        frontmatter.Parsed.Should().BeFalse();
        frontmatter.Error.Should().Contain("never closed");
    }

    [Test]
    public void Parse_LineThatIsNeitherKeyNorListItem_IsRejected()
    {
        var frontmatter = Parse("status: accepted", "this is prose");

        frontmatter.Parsed.Should().BeFalse();
        frontmatter.Error.Should().Contain("neither 'key: value' nor a '- item'");
    }

    [Test]
    public void Parse_ListItemWithNoPrecedingKey_IsRejected()
    {
        var frontmatter = Parse("- orphaned");

        frontmatter.Parsed.Should().BeFalse();
        frontmatter.Error.Should().Contain("does not follow a key");
    }

    [Test]
    public void Parse_DuplicateKey_IsRejected()
    {
        var frontmatter = Parse("status: accepted", "status: proposed");

        frontmatter.Parsed.Should().BeFalse();
        frontmatter.Error.Should().Contain("appears twice");
    }

    [Test]
    public void Parse_IndentedKey_IsRejected()
    {
        var frontmatter = Parse("status: accepted", "  nested: value");

        frontmatter.Parsed.Should().BeFalse();
        frontmatter.Error.Should().Contain("must not be indented");
    }

    [Test]
    public void Parse_UnterminatedFlowList_IsRejected()
    {
        var frontmatter = Parse("areas: [migrations");

        frontmatter.Parsed.Should().BeFalse();
        frontmatter.Error.Should().Contain("unterminated flow list");
    }

    #endregion
}
