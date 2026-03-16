using FluentAssertions;
using Trax.Cli.Generator;

namespace Trax.Cli.Tests.UnitTests;

[TestFixture]
public class NamingConventionsTests
{
    #region ToPascalCase

    [Test]
    public void ToPascalCase_CamelCase_ReturnsPascalCase()
    {
        NamingConventions.ToPascalCase("camelCase").Should().Be("CamelCase");
    }

    [Test]
    public void ToPascalCase_SnakeCase_ReturnsPascalCase()
    {
        NamingConventions.ToPascalCase("snake_case").Should().Be("SnakeCase");
    }

    [Test]
    public void ToPascalCase_KebabCase_ReturnsPascalCase()
    {
        NamingConventions.ToPascalCase("kebab-case").Should().Be("KebabCase");
    }

    [Test]
    public void ToPascalCase_AlreadyPascalCase_Unchanged()
    {
        NamingConventions.ToPascalCase("PascalCase").Should().Be("PascalCase");
    }

    [Test]
    public void ToPascalCase_AllCaps_ReturnsPascalcase()
    {
        NamingConventions.ToPascalCase("ALL_CAPS").Should().Be("AllCaps");
    }

    #endregion

    #region ToCamelCase

    [Test]
    public void ToCamelCase_PascalCase_ReturnsCamelCase()
    {
        NamingConventions.ToCamelCase("PascalCase").Should().Be("pascalCase");
    }

    #endregion

    #region SanitizeIdentifier

    [Test]
    public void SanitizeIdentifier_CSharpKeywordClass_PrependAt()
    {
        NamingConventions.SanitizeIdentifier("class").Should().Be("@class");
    }

    [Test]
    public void SanitizeIdentifier_CSharpKeywordEvent_PrependAt()
    {
        NamingConventions.SanitizeIdentifier("event").Should().Be("@event");
    }

    [Test]
    public void SanitizeIdentifier_NonKeyword_Unchanged()
    {
        NamingConventions.SanitizeIdentifier("name").Should().Be("name");
    }

    #endregion

    #region ToKebabCase

    [Test]
    public void ToKebabCase_PascalCase_ReturnsKebabCase()
    {
        NamingConventions.ToKebabCase("GetPlayer").Should().Be("get-player");
    }

    [Test]
    public void ToKebabCase_CamelCase_ReturnsKebabCase()
    {
        NamingConventions.ToKebabCase("createPlayer").Should().Be("create-player");
    }

    [Test]
    public void ToKebabCase_SingleWord_ReturnsLowerCase()
    {
        NamingConventions.ToKebabCase("Search").Should().Be("search");
    }

    [Test]
    public void ToKebabCase_AlreadyKebabCase_Unchanged()
    {
        NamingConventions.ToKebabCase("already-kebab").Should().Be("already-kebab");
    }

    [Test]
    public void ToKebabCase_EmptyString_ReturnsEmpty()
    {
        NamingConventions.ToKebabCase("").Should().Be("");
    }

    #endregion

    #region DeriveGroupName

    [Test]
    public void DeriveGroupName_CreatePlayer_ReturnsPlayers()
    {
        NamingConventions.DeriveGroupName("createPlayer").Should().Be("Players");
    }

    [Test]
    public void DeriveGroupName_GetUser_ReturnsUsers()
    {
        NamingConventions.DeriveGroupName("getUser").Should().Be("Users");
    }

    [Test]
    public void DeriveGroupName_Search_ReturnsSearchs()
    {
        NamingConventions.DeriveGroupName("search").Should().Be("Searchs");
    }

    [Test]
    public void DeriveGroupName_ListItems_ReturnsItems()
    {
        NamingConventions.DeriveGroupName("listItems").Should().Be("Items");
    }

    [Test]
    public void DeriveGroupName_DeleteEntry_ReturnsEntries()
    {
        NamingConventions.DeriveGroupName("deleteEntry").Should().Be("Entries");
    }

    #endregion
}
