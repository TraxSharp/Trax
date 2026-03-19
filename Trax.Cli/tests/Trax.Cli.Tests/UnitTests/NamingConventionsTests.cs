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

    #region StripHttpVerbPrefix

    [Test]
    public void StripHttpVerbPrefix_GetPlayer_ReturnsPlayer()
    {
        NamingConventions.StripHttpVerbPrefix("GetPlayer").Should().Be("Player");
    }

    [Test]
    public void StripHttpVerbPrefix_PostLogin_ReturnsLogin()
    {
        NamingConventions.StripHttpVerbPrefix("PostLogin").Should().Be("Login");
    }

    [Test]
    public void StripHttpVerbPrefix_PutSettings_ReturnsSettings()
    {
        NamingConventions.StripHttpVerbPrefix("PutSettings").Should().Be("Settings");
    }

    [Test]
    public void StripHttpVerbPrefix_PatchProfile_ReturnsProfile()
    {
        NamingConventions.StripHttpVerbPrefix("PatchProfile").Should().Be("Profile");
    }

    [Test]
    public void StripHttpVerbPrefix_DeleteUser_ReturnsUser()
    {
        NamingConventions.StripHttpVerbPrefix("DeleteUser").Should().Be("User");
    }

    [Test]
    public void StripHttpVerbPrefix_ListItems_ReturnsListItems()
    {
        // "List" is not an HTTP verb — should be left alone
        NamingConventions.StripHttpVerbPrefix("ListItems").Should().Be("ListItems");
    }

    [Test]
    public void StripHttpVerbPrefix_CreateUser_ReturnsCreateUser()
    {
        // "Create" is not an HTTP verb — should be left alone
        NamingConventions.StripHttpVerbPrefix("CreateUser").Should().Be("CreateUser");
    }

    [Test]
    public void StripHttpVerbPrefix_SearchItems_ReturnsSearchItems()
    {
        NamingConventions.StripHttpVerbPrefix("SearchItems").Should().Be("SearchItems");
    }

    [Test]
    public void StripHttpVerbPrefix_Get_ReturnsGet()
    {
        // "Get" alone with nothing after — should not be stripped
        NamingConventions.StripHttpVerbPrefix("Get").Should().Be("Get");
    }

    [Test]
    public void StripHttpVerbPrefix_Getting_ReturnsGetting()
    {
        // "Getting" — next char is lowercase, not a PascalCase boundary
        NamingConventions.StripHttpVerbPrefix("Getting").Should().Be("Getting");
    }

    [Test]
    public void StripHttpVerbPrefix_Postman_ReturnsPostman()
    {
        // "Postman" — next char after "Post" is lowercase
        NamingConventions.StripHttpVerbPrefix("Postman").Should().Be("Postman");
    }

    [Test]
    public void StripHttpVerbPrefix_EmptyString_ReturnsEmpty()
    {
        NamingConventions.StripHttpVerbPrefix("").Should().Be("");
    }

    [Test]
    public void StripHttpVerbPrefix_NoPrefixMatch_ReturnsOriginal()
    {
        NamingConventions.StripHttpVerbPrefix("FetchData").Should().Be("FetchData");
    }

    #endregion

    #region SimplifySchemaName

    [Test]
    public void SimplifySchemaName_FullyQualifiedDotNetType_ReturnsLastSegment()
    {
        NamingConventions
            .SimplifySchemaName("MyApp.Domain.Users.DTOs.UserDto")
            .Should()
            .Be("UserDto");
    }

    [Test]
    public void SimplifySchemaName_DeeplyNested_ReturnsLastSegment()
    {
        NamingConventions
            .SimplifySchemaName(
                "AdvocacyDay.CVLegacy.Domain.Bills.GetBill.DTOs.GetBillBillVotesTopicsDto"
            )
            .Should()
            .Be("GetBillBillVotesTopicsDto");
    }

    [Test]
    public void SimplifySchemaName_NoDots_ReturnsUnchanged()
    {
        NamingConventions.SimplifySchemaName("UserDto").Should().Be("UserDto");
    }

    [Test]
    public void SimplifySchemaName_EmptyString_ReturnsEmpty()
    {
        NamingConventions.SimplifySchemaName("").Should().Be("");
    }

    [Test]
    public void SimplifySchemaName_SingleDot_ReturnsAfterDot()
    {
        NamingConventions.SimplifySchemaName("Namespace.Type").Should().Be("Type");
    }

    #endregion

    #region ToPascalCase_SpecialCharacters

    [Test]
    public void ToPascalCase_DottedName_SplitsOnDots()
    {
        NamingConventions.ToPascalCase("calendar.ics").Should().Be("CalendarIcs");
    }

    [Test]
    public void ToPascalCase_CommaInName_SplitsOnCommas()
    {
        NamingConventions.ToPascalCase("meetings,intents").Should().Be("MeetingsIntents");
    }

    [Test]
    public void ToPascalCase_MultipleSeparators_SplitsAll()
    {
        NamingConventions.ToPascalCase("a.b,c-d_e f").Should().Be("ABCDEF");
    }

    #endregion
}
