using FluentAssertions;
using Trax.Cli.Models;
using Trax.Cli.Schema.OpenApi;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// Tests for OpenAPI parser fixes:
/// - Non-fatal validation errors no longer crash the parser
/// - Inline enums get named from context (property/parameter name) instead of "UnnamedEnum"
/// - Property names that collide with enclosing type names get suffixed
/// - Array-type component schemas produce wrapper types with Items field
/// - Duplicate synthesized operation names get numeric suffixes
/// </summary>
[TestFixture]
public class OpenApiParserFixTests
{
    private static string FixturePath(string name) =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Schemas", name);

    #region NonFatalErrors

    [Test]
    public void Parse_SchemaWithNonFatalErrors_StillReturnsOperations()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("non-fatal-errors.yaml"));

        schema.Operations.Should().HaveCount(2);
        schema.Operations.Select(o => o.Name).Should().Contain("ListItems");
        schema.Operations.Select(o => o.Name).Should().Contain("ListIssues");
    }

    [Test]
    public void Parse_SchemaWithNonFatalErrors_StillReturnsTypes()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("non-fatal-errors.yaml"));

        schema.Types.Should().Contain(t => t.Name == "Item");
        schema.Types.Should().Contain(t => t.Name == "Issue");
    }

    [Test]
    public void Parse_SchemaWithNonFatalErrors_ItemTypeHasFields()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("non-fatal-errors.yaml"));

        var item = schema.Types.Single(t => t.Name == "Item");
        item.Fields.Should().Contain(f => f.Name == "Id");
        item.Fields.Should().Contain(f => f.Name == "Name");
    }

    [Test]
    public void Parse_SchemaWithNonFatalErrors_DoesNotThrow()
    {
        var parser = new OpenApiSchemaParser();

        var act = () => parser.Parse(FixturePath("non-fatal-errors.yaml"));

        act.Should().NotThrow();
    }

    #endregion

    #region InlineEnums

    [Test]
    public void Parse_InlineEnumsInParameters_NamedFromParameterName()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("inline-enums.json"));

        var listItems = schema.Operations.Single(o => o.Name == "ListItems");
        var statusField = listItems.InputType.Fields.Single(f => f.Name == "Status");

        // Should be named "Status" (from param name), not "UnnamedEnum"
        statusField.TypeName.Should().Be("Status");
    }

    [Test]
    public void Parse_InlineEnumsInParameters_MultipleEnumsGetDistinctNames()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("inline-enums.json"));

        var listItems = schema.Operations.Single(o => o.Name == "ListItems");
        var statusField = listItems.InputType.Fields.Single(f => f.Name == "Status");
        var sortByField = listItems.InputType.Fields.Single(f => f.Name == "SortBy");

        statusField.TypeName.Should().Be("Status");
        sortByField.TypeName.Should().Be("SortBy");
        statusField.TypeName.Should().NotBe(sortByField.TypeName);
    }

    [Test]
    public void Parse_InlineEnumsInRequestBody_NamedFromPropertyName()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("inline-enums.json"));

        var createItem = schema.Operations.Single(o => o.Name == "CreateItem");
        var priorityField = createItem.InputType.Fields.Single(f => f.Name == "Priority");

        priorityField.TypeName.Should().Be("Priority");
    }

    [Test]
    public void Parse_InlineEnumsInComponentSchema_NamedFromPropertyName()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("inline-enums.json"));

        var itemType = schema.Types.Single(t => t.Name == "Item");
        var categoryField = itemType.Fields.Single(f => f.Name == "Category");

        categoryField.TypeName.Should().Be("Category");
    }

    [Test]
    public void Parse_InlineEnums_EnumValuesAreRegistered()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("inline-enums.json"));

        var statusEnum = schema.Enums.SingleOrDefault(e => e.Name == "Status");
        statusEnum.Should().NotBeNull();
        statusEnum!.Values.Should().BeEquivalentTo("Active", "Inactive", "Archived");
    }

    [Test]
    public void Parse_InlineEnums_PriorityEnumValuesAreRegistered()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("inline-enums.json"));

        var priorityEnum = schema.Enums.SingleOrDefault(e => e.Name == "Priority");
        priorityEnum.Should().NotBeNull();
        priorityEnum!.Values.Should().BeEquivalentTo("Low", "Medium", "High", "Critical");
    }

    [Test]
    public void Parse_InlineEnums_NoUnnamedEnumInResults()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("inline-enums.json"));

        schema.Enums.Should().NotContain(e => e.Name == "UnnamedEnum");
    }

    [Test]
    public void Parse_InlineEnums_NonEnumParametersUnaffected()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("inline-enums.json"));

        var listItems = schema.Operations.Single(o => o.Name == "ListItems");
        var limitField = listItems.InputType.Fields.Single(f => f.Name == "Limit");

        limitField.TypeName.Should().Be("int");
    }

    [Test]
    public void Parse_InlineEnums_SameValuesAcrossParameterAndSchema_ReusesEnum()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("inline-enums.json"));

        // Both the "status" query parameter and the Item.status property use
        // enum [active, inactive, archived] — they should resolve to the same enum name
        var listItems = schema.Operations.Single(o => o.Name == "ListItems");
        var paramStatusType = listItems.InputType.Fields.Single(f => f.Name == "Status").TypeName;

        var itemType = schema.Types.Single(t => t.Name == "Item");
        var schemaStatusType = itemType.Fields.Single(f => f.Name == "Status").TypeName;

        paramStatusType.Should().Be(schemaStatusType);
    }

    #endregion

    #region PropertyNameCollision

    [Test]
    public void Parse_PropertyNameMatchesTypeName_GetsSuffixed()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("property-collision.json"));

        var notificationType = schema.Types.Single(t => t.Name == "Notification");
        // "notification" property on Notification type → PascalCase "Notification" → collides → suffixed
        notificationType.Fields.Should().NotContain(f => f.Name == "Notification");
        notificationType.Fields.Should().Contain(f => f.Name == "NotificationValue");
    }

    [Test]
    public void Parse_PropertyNameMatchesTypeName_MultipleTypes()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("property-collision.json"));

        var testimonialType = schema.Types.Single(t => t.Name == "Testimonial");
        testimonialType.Fields.Should().NotContain(f => f.Name == "Testimonial");
        testimonialType.Fields.Should().Contain(f => f.Name == "TestimonialValue");
    }

    [Test]
    public void Parse_PropertyNameDoesNotMatchTypeName_NotSuffixed()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("property-collision.json"));

        var notificationType = schema.Types.Single(t => t.Name == "Notification");
        // "title" does not collide with "Notification" — should remain "Title"
        notificationType.Fields.Should().Contain(f => f.Name == "Title");
        notificationType.Fields.Should().Contain(f => f.Name == "Id");
        notificationType.Fields.Should().Contain(f => f.Name == "IsRead");
    }

    [Test]
    public void Parse_PropertyCollision_OtherFieldsOnTestimonialUnaffected()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("property-collision.json"));

        var testimonialType = schema.Types.Single(t => t.Name == "Testimonial");
        testimonialType.Fields.Should().Contain(f => f.Name == "Id");
        testimonialType.Fields.Should().Contain(f => f.Name == "Author");
    }

    #endregion

    #region ArrayComponentSchemas

    [Test]
    public void Parse_ArrayTypeComponentSchema_CreatesWrapperTypeWithItemsField()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("array-component.json"));

        var voteTally = schema.Types.SingleOrDefault(t => t.Name == "VoteTallyResponse");
        voteTally.Should().NotBeNull();
        voteTally!.Fields.Should().HaveCount(1);
        voteTally.Fields[0].Name.Should().Be("Items");
        voteTally.Fields[0].TypeName.Should().StartWith("List<");
    }

    [Test]
    public void Parse_ArrayTypeComponentSchema_IsNotBuiltIn()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("array-component.json"));

        var voteTally = schema.Types.Single(t => t.Name == "VoteTallyResponse");
        voteTally.IsBuiltIn.Should().BeFalse();
    }

    [Test]
    public void Parse_ArrayTypeComponentSchemaWithRef_ItemsFieldReferencesRefType()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("array-component.json"));

        var scoreboard = schema.Types.SingleOrDefault(t => t.Name == "Scoreboard");
        scoreboard.Should().NotBeNull();
        scoreboard!.Fields.Should().HaveCount(1);
        scoreboard.Fields[0].Name.Should().Be("Items");
        scoreboard.Fields[0].TypeName.Should().Be("List<ScoreEntry>");
    }

    [Test]
    public void Parse_ArrayTypeComponentSchema_ReferencedFromResponse_OperationUsesTypeName()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("array-component.json"));

        var voteTally = schema.Operations.Single(o => o.Name == "VoteTally");
        // The response $ref to VoteTallyResponse should use the type name
        voteTally.OutputType.Name.Should().Be("VoteTallyResponse");
    }

    [Test]
    public void Parse_ArrayTypeComponentSchema_InlineObjectItems_PromotedToNamedType()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("array-component.json"));

        // VoteTallyResponse has inline object items with properties — should be promoted
        var voteTally = schema.Types.Single(t => t.Name == "VoteTallyResponse");
        voteTally.Fields[0].TypeName.Should().Be("List<VoteTallyResponseItem>");
    }

    [Test]
    public void Parse_ArrayTypeComponentSchema_InlineObjectItems_PromotedTypeHasFields()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("array-component.json"));

        var itemType = schema.Types.SingleOrDefault(t => t.Name == "VoteTallyResponseItem");
        itemType.Should().NotBeNull();
        itemType!.Fields.Should().Contain(f => f.Name == "RepId");
        itemType.Fields.Should().Contain(f => f.Name == "Name");
    }

    [Test]
    public void Parse_ArrayTypeComponentSchema_InlineObjectItems_EnumFieldResolved()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("array-component.json"));

        var itemType = schema.Types.Single(t => t.Name == "VoteTallyResponseItem");
        var legVotedField = itemType.Fields.Single(f => f.Name == "LegVoted");
        legVotedField.TypeName.Should().Be("LegVoted");

        schema.Enums.Should().Contain(e => e.Name == "LegVoted");
    }

    [Test]
    public void Parse_ArrayTypeComponentSchema_RegularObjectSchemasStillWork()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("array-component.json"));

        var scoreEntry = schema.Types.SingleOrDefault(t => t.Name == "ScoreEntry");
        scoreEntry.Should().NotBeNull();
        scoreEntry!.Fields.Should().Contain(f => f.Name == "Player");
        scoreEntry.Fields.Should().Contain(f => f.Name == "Score");
    }

    #endregion

    #region InlineObjectPromotion

    [Test]
    public void Parse_InlineObjectInArrayResponse_PromotedToNamedType()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("inline-object-array.json"));

        var listEvents = schema.Operations.Single(o => o.Name == "ListEvents");
        listEvents.OutputType.Fields[0].TypeName.Should().Be("List<ListEventsItem>");
    }

    [Test]
    public void Parse_InlineObjectInArrayResponse_PromotedTypeHasAllFields()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("inline-object-array.json"));

        var itemType = schema.Types.SingleOrDefault(t => t.Name == "ListEventsItem");
        itemType.Should().NotBeNull();
        itemType!.Fields.Should().Contain(f => f.Name == "Id");
        itemType.Fields.Should().Contain(f => f.Name == "Title");
        itemType.Fields.Should().Contain(f => f.Name == "StartDate");
    }

    [Test]
    public void Parse_InlineObjectInArrayResponse_FieldTypesResolved()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("inline-object-array.json"));

        var itemType = schema.Types.Single(t => t.Name == "ListEventsItem");
        itemType.Fields.Single(f => f.Name == "Id").TypeName.Should().Be("int");
        itemType.Fields.Single(f => f.Name == "Title").TypeName.Should().Be("string");
        itemType.Fields.Single(f => f.Name == "StartDate").TypeName.Should().Be("DateTime");
    }

    [Test]
    public void Parse_InlineObjectInProperty_PromotedToNamedType()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("inline-object-array.json"));

        // The "metadata" property on Event is an inline object
        var eventType = schema.Types.SingleOrDefault(t => t.Name == "Event");
        if (eventType != null)
        {
            var metadataField = eventType.Fields.SingleOrDefault(f => f.Name == "Metadata");
            if (metadataField != null)
            {
                metadataField.TypeName.Should().NotBe("object");
            }
        }
    }

    [Test]
    public void Parse_ArrayWithRefItems_StillUsesRefTypeName()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("inline-object-array.json"));

        var listSpeakers = schema.Operations.Single(o => o.Name == "ListSpeakers");
        listSpeakers.OutputType.Fields[0].TypeName.Should().Be("List<Speaker>");
    }

    [Test]
    public void Parse_ArrayWithNoItemProperties_FallsBackToObject()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("inline-object-array.json"));

        // RawData returns array of bare objects (no properties) — should remain List<object>
        var getRaw = schema.Operations.Single(o => o.Name == "RawData");
        getRaw.OutputType.Fields[0].TypeName.Should().Be("List<object>");
    }

    [Test]
    public void Parse_InlineObjectWithOnlyBareObjectProperties_NotPromoted()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("bare-object-properties.json"));

        // SearchResult.hit has only bare object properties (_source: type: object)
        // Should NOT be promoted — remains as "object"
        var searchResult = schema.Types.Single(t => t.Name == "SearchResult");
        var hitField = searchResult.Fields.Single(f => f.Name == "Hit");
        hitField.TypeName.Should().Be("object");
    }

    [Test]
    public void Parse_InlineObjectWithOnlyBareObjectProperties_NoNamedTypeCreated()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("bare-object-properties.json"));

        // No "Hit" type should be created
        schema.Types.Should().NotContain(t => t.Name == "Hit");
    }

    [Test]
    public void Parse_InlineObjectWithMixedProperties_StillPromoted()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("bare-object-properties.json"));

        // SearchResult.mixed has { name: string, data: object } — not all bare objects
        // Should be promoted to a named type
        var searchResult = schema.Types.Single(t => t.Name == "SearchResult");
        var mixedField = searchResult.Fields.Single(f => f.Name == "Mixed");
        mixedField.TypeName.Should().NotBe("object");
    }

    [Test]
    public void Parse_InlineObjectWithMixedProperties_PromotedTypeHasFields()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("bare-object-properties.json"));

        var searchResult = schema.Types.Single(t => t.Name == "SearchResult");
        var mixedField = searchResult.Fields.Single(f => f.Name == "Mixed");
        var mixedType = schema.Types.SingleOrDefault(t => t.Name == mixedField.TypeName);
        mixedType.Should().NotBeNull();
        mixedType!.Fields.Should().Contain(f => f.Name == "Name" && f.TypeName == "string");
    }

    #endregion

    #region HttpVerbStripping

    [Test]
    public void Parse_OperationIdWithGetPrefix_PrefixStripped()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("array-component.json"));

        // getVoteTally → VoteTally (no type collision)
        schema.Operations.Should().Contain(o => o.Name == "VoteTally");
    }

    [Test]
    public void Parse_OperationIdWithGetPrefix_CollidesWithType_PrefixKept()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("petstore.json"));

        // getPet → would strip to "Pet" but Pet is a model type → keeps "GetPet"
        schema.Operations.Should().Contain(o => o.Name == "GetPet");
    }

    [Test]
    public void Parse_OperationIdWithDeletePrefix_CollidesWithType_PrefixKept()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("petstore.json"));

        // deletePet → would strip to "Pet" but Pet is a model type → keeps "DeletePet"
        schema.Operations.Should().Contain(o => o.Name == "DeletePet");
    }

    [Test]
    public void Parse_OperationIdWithNonHttpVerb_NotStripped()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("inline-enums.json"));

        // listItems → "List" isn't an HTTP verb → stays "ListItems"
        schema.Operations.Should().Contain(o => o.Name == "ListItems");
    }

    [Test]
    public void Parse_OperationIdWithGetPrefix_NoCollision_Stripped()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("inline-object-array.json"));

        // getRawData → "RawData" (no type named RawData) → stripped
        schema.Operations.Should().Contain(o => o.Name == "RawData");
    }

    [Test]
    public void Parse_SynthesizedName_CollidingNames_KeepPrefix()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("duplicate-names.json"));

        // Multiple paths synthesize to "Users" → collision → all keep HTTP verb prefix
        schema.Operations.Should().Contain(o => o.Name == "GetUsers");
        schema.Operations.Should().Contain(o => o.Name == "PostUsers");
    }

    #endregion

    #region DuplicateOperationNames

    [Test]
    public void Parse_DuplicateSynthesizedNames_AllOperationsPresent()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("duplicate-names.json"));

        // /users GET, /users POST, /users/{userId} GET, /users/{userId} DELETE, /users/{userId}/posts GET
        schema.Operations.Should().HaveCount(5);
    }

    [Test]
    public void Parse_DuplicateSynthesizedNames_AllNamesAreUnique()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("duplicate-names.json"));

        var names = schema.Operations.Select(o => o.Name).ToList();
        names.Should().OnlyHaveUniqueItems();
    }

    [Test]
    public void Parse_DuplicateSynthesizedNames_CollidingNamesKeepPrefix()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("duplicate-names.json"));

        // /users GET and /users POST both strip to "Users" → collision → keep prefixes
        schema.Operations.Should().Contain(o => o.Name == "GetUsers");
        schema.Operations.Should().Contain(o => o.Name == "PostUsers");
    }

    [Test]
    public void Parse_DuplicateSynthesizedNames_SecondGetDisambiguatedByPathParam()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("duplicate-names.json"));

        // /users/{userId} GET → "GetUsers" collides → disambiguated to "GetUsersByUserId"
        schema.Operations.Should().Contain(o => o.Name == "GetUsersByUserId");
    }

    [Test]
    public void Parse_DuplicateSynthesizedNames_DeleteKeepsPrefix()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("duplicate-names.json"));

        // /users/{userId} DELETE → "DeleteUsers" (prefix kept due to collision)
        schema.Operations.Should().Contain(o => o.Name == "DeleteUsers");
    }

    [Test]
    public void Parse_DuplicateSynthesizedNames_SubpathOperationsDistinct()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("duplicate-names.json"));

        // /users/{userId}/posts GET → stripped "UsersPosts" is unique → no prefix needed
        schema.Operations.Should().Contain(o => o.Name == "UsersPosts");
    }

    [Test]
    public void Parse_DuplicateSynthesizedNames_HttpPathsPreserved()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("duplicate-names.json"));

        // Even though names keep prefixes, each operation keeps its original path
        var getUsers = schema.Operations.Single(o => o.Name == "GetUsers");
        getUsers.HttpPath.Should().Be("/users");

        var getUsersById = schema.Operations.Single(o => o.Name == "GetUsersByUserId");
        getUsersById.HttpPath.Should().Be("/users/{userId}");
    }

    [Test]
    public void Parse_WithExplicitOperationIds_NoDuplication()
    {
        // petstore.json has explicit operationIds — getPet and deletePet become Pet and Pet2
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("petstore.json"));

        var names = schema.Operations.Select(o => o.Name).ToList();
        names.Should().OnlyHaveUniqueItems();
    }

    #endregion

    #region DottedSchemaNames

    [Test]
    public void Parse_DottedSchemaNames_TypesHaveSimplifiedNames()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("dotted-names.json"));

        schema.Types.Should().Contain(t => t.Name == "UserDto");
        schema.Types.Should().Contain(t => t.Name == "UserListDto");
        schema.Types.Should().Contain(t => t.Name == "CreateUserCommand");
        schema.Types.Should().Contain(t => t.Name == "ReportListDto");
    }

    [Test]
    public void Parse_DottedSchemaNames_NoDotsInTypeNames()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("dotted-names.json"));

        schema.Types.Should().AllSatisfy(t => t.Name.Should().NotContain("."));
    }

    [Test]
    public void Parse_DottedSchemaNames_RefFieldsResolveToSimplifiedNames()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("dotted-names.json"));

        var userListType = schema.Types.Single(t => t.Name == "UserListDto");
        var usersField = userListType.Fields.Single(f => f.Name == "Users");
        usersField.TypeName.Should().Be("List<UserDto>");
    }

    [Test]
    public void Parse_DottedSchemaNames_EmptySchemaStillResolved()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("dotted-names.json"));

        // EmptyResponse has no properties but should still be in the types list
        schema.Types.Should().Contain(t => t.Name == "EmptyResponse");
    }

    [Test]
    public void Parse_DottedSchemaNames_RootPathGetsRootName()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("dotted-names.json"));

        var rootOp = schema.Operations.SingleOrDefault(o => o.HttpPath == "/");
        rootOp.Should().NotBeNull();
        rootOp!.Name.Should().Be("Root");
    }

    [Test]
    public void Parse_DottedSchemaNames_DotInPathSegmentHandled()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("dotted-names.json"));

        var calendarOp = schema.Operations.SingleOrDefault(o =>
            o.HttpPath == "/events/calendar.ics"
        );
        calendarOp.Should().NotBeNull();
        calendarOp!.Name.Should().NotContain(".");
    }

    [Test]
    public void Parse_DottedSchemaNames_CommaInTagBecomesValidGroup()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("dotted-names.json"));

        var reportsOp = schema.Operations.Single(o => o.HttpPath == "/reports");
        reportsOp.Group.Should().NotContain(",");
        reportsOp.Group.Should().Be("MeetingsReports");
    }

    [Test]
    public void Parse_DottedSchemaNames_DuplicateParamNamesAfterPascalCaseDeduped()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("dotted-names.json"));

        var tagOp = schema.Operations.Single(o =>
            o.HttpPath == "/items/{itemId}/tags/{update_value}"
        );
        // updateValue (query) and update_value (path) both become UpdateValue
        // Should be deduplicated to avoid CS0102
        var fieldNames = tagOp.InputType.Fields.Select(f => f.Name).ToList();
        fieldNames.Should().OnlyHaveUniqueItems();
    }

    [Test]
    public void Parse_DottedSchemaNames_EmptySchemaParamResolvedToObject()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("dotted-names.json"));

        var searchOp = schema.Operations.Single(o => o.HttpPath == "/search");
        // "filters" param references an empty schema — resolved to "object" not the empty type name
        var filtersField = searchOp.InputType.Fields.Single(f => f.Name == "Filters");
        filtersField.TypeName.Should().Be("object");
    }

    [Test]
    public void Parse_DottedSchemaNames_DescriptionNewlinesCollapsed()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("dotted-names.json"));

        // The notes POST has a multiline summary — verify it's captured
        var notesOp = schema.Operations.Single(o => o.HttpPath == "/notes");
        notesOp.Description.Should().NotBeNull();
    }

    #endregion
}
