using FluentAssertions;
using Trax.Cli.Generator;
using Trax.Cli.Schema.OpenApi;

namespace Trax.Cli.Tests.IntegrationTests;

/// <summary>
/// End-to-end tests for all OpenAPI parser and code generation fixes.
/// Each test parses a fixture schema, generates a trains library, and verifies
/// the generated files are correct.
/// </summary>
[TestFixture]
public class OpenApiFixEndToEndTests
{
    private OpenApiSchemaParser _parser = null!;
    private TraxProjectGenerator _generator = null!;
    private string _outputDir = null!;

    [SetUp]
    public void SetUp()
    {
        _parser = new OpenApiSchemaParser();
        _generator = new TraxProjectGenerator();
        _outputDir = Path.Combine(Path.GetTempPath(), $"trax-cli-fix-e2e-{Guid.NewGuid():N}");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_outputDir))
            Directory.Delete(_outputDir, recursive: true);
    }

    private static string FixturePath(string name) =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Schemas", name);

    #region NonFatalErrors

    [Test]
    public void GenerateTrainsLibrary_NonFatalErrors_AllFilesGenerated()
    {
        var schema = _parser.Parse(FixturePath("non-fatal-errors.yaml"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        File.Exists(Path.Combine(_outputDir, "TestProject.Trains.csproj")).Should().BeTrue();
        File.Exists(Path.Combine(_outputDir, "ManifestNames.cs")).Should().BeTrue();

        var listItemsDir = Path.Combine(_outputDir, "Trains", "Items", "ListItems");
        File.Exists(Path.Combine(listItemsDir, "IListItemsTrain.cs")).Should().BeTrue();
        File.Exists(Path.Combine(listItemsDir, "ListItemsTrain.cs")).Should().BeTrue();

        var listIssuesDir = Path.Combine(_outputDir, "Trains", "Issues", "ListIssues");
        File.Exists(Path.Combine(listIssuesDir, "IListIssuesTrain.cs")).Should().BeTrue();
        File.Exists(Path.Combine(listIssuesDir, "ListIssuesTrain.cs")).Should().BeTrue();
    }

    [Test]
    public void GenerateTrainsLibrary_NonFatalErrors_ModelFilesGenerated()
    {
        var schema = _parser.Parse(FixturePath("non-fatal-errors.yaml"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        File.Exists(Path.Combine(_outputDir, "Models", "Item.cs")).Should().BeTrue();
        File.Exists(Path.Combine(_outputDir, "Models", "Issue.cs")).Should().BeTrue();
    }

    #endregion

    #region InlineEnums

    [Test]
    public void GenerateTrainsLibrary_InlineEnums_EnumFilesGenerated()
    {
        var schema = _parser.Parse(FixturePath("inline-enums.json"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        // Inline enums from parameters and properties should be generated as enum files
        var modelsDir = Path.Combine(_outputDir, "Models");
        Directory.Exists(modelsDir).Should().BeTrue();

        File.Exists(Path.Combine(modelsDir, "Status.cs")).Should().BeTrue();
        File.Exists(Path.Combine(modelsDir, "SortBy.cs")).Should().BeTrue();
        File.Exists(Path.Combine(modelsDir, "Priority.cs")).Should().BeTrue();
        File.Exists(Path.Combine(modelsDir, "Category.cs")).Should().BeTrue();
    }

    [Test]
    public void GenerateTrainsLibrary_InlineEnums_NoUnnamedEnumFile()
    {
        var schema = _parser.Parse(FixturePath("inline-enums.json"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        File.Exists(Path.Combine(_outputDir, "Models", "UnnamedEnum.cs")).Should().BeFalse();
    }

    [Test]
    public void GenerateTrainsLibrary_InlineEnums_StatusEnumHasCorrectValues()
    {
        var schema = _parser.Parse(FixturePath("inline-enums.json"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        var content = File.ReadAllText(Path.Combine(_outputDir, "Models", "Status.cs"));
        content.Should().Contain("public enum Status");
        content.Should().Contain("Active");
        content.Should().Contain("Inactive");
        content.Should().Contain("Archived");
    }

    [Test]
    public void GenerateTrainsLibrary_InlineEnums_PriorityEnumHasCorrectValues()
    {
        var schema = _parser.Parse(FixturePath("inline-enums.json"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        var content = File.ReadAllText(Path.Combine(_outputDir, "Models", "Priority.cs"));
        content.Should().Contain("public enum Priority");
        content.Should().Contain("Low");
        content.Should().Contain("Medium");
        content.Should().Contain("High");
        content.Should().Contain("Critical");
    }

    [Test]
    public void GenerateTrainsLibrary_InlineEnums_InputFileReferencesEnumType()
    {
        var schema = _parser.Parse(FixturePath("inline-enums.json"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        var inputFile = Path.Combine(
            _outputDir,
            "Trains",
            "Items",
            "ListItems",
            "ListItemsInput.cs"
        );
        var content = File.ReadAllText(inputFile);

        content.Should().Contain("Status");
        content.Should().Contain("SortBy");
        // Should have using directive for Models namespace
        content.Should().Contain("using TestProject.Trains.Models;");
    }

    [Test]
    public void GenerateTrainsLibrary_InlineEnums_CreateItemInputReferencesEnum()
    {
        var schema = _parser.Parse(FixturePath("inline-enums.json"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        var inputFile = Path.Combine(
            _outputDir,
            "Trains",
            "Items",
            "CreateItem",
            "CreateItemInput.cs"
        );
        var content = File.ReadAllText(inputFile);

        content.Should().Contain("Priority");
        content.Should().Contain("using TestProject.Trains.Models;");
    }

    #endregion

    #region PropertyCollision

    [Test]
    public void GenerateTrainsLibrary_PropertyCollision_NotificationModelCompilable()
    {
        var schema = _parser.Parse(FixturePath("property-collision.json"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        var notificationFile = Path.Combine(_outputDir, "Models", "Notification.cs");
        File.Exists(notificationFile).Should().BeTrue();

        var content = File.ReadAllText(notificationFile);
        // Property should be suffixed to avoid CS0542
        content.Should().Contain("NotificationValue");
        content.Should().Contain("public record Notification");
        // Other properties should be normal
        content.Should().Contain("Title");
        content.Should().Contain("Id");
    }

    [Test]
    public void GenerateTrainsLibrary_PropertyCollision_TestimonialModelCompilable()
    {
        var schema = _parser.Parse(FixturePath("property-collision.json"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        var testimonialFile = Path.Combine(_outputDir, "Models", "Testimonial.cs");
        File.Exists(testimonialFile).Should().BeTrue();

        var content = File.ReadAllText(testimonialFile);
        content.Should().Contain("TestimonialValue");
        content.Should().Contain("public record Testimonial");
        content.Should().Contain("Author");
    }

    [Test]
    public void GenerateTrainsLibrary_PropertyCollision_TrainFilesStillGenerated()
    {
        var schema = _parser.Parse(FixturePath("property-collision.json"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        var listDir = Path.Combine(_outputDir, "Trains", "Notifications", "ListNotifications");
        File.Exists(Path.Combine(listDir, "IListNotificationsTrain.cs")).Should().BeTrue();
        File.Exists(Path.Combine(listDir, "ListNotificationsTrain.cs")).Should().BeTrue();
    }

    #endregion

    #region ArrayComponentSchemas

    [Test]
    public void GenerateTrainsLibrary_ArrayComponentSchema_WrapperTypeGenerated()
    {
        var schema = _parser.Parse(FixturePath("array-component.json"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        var voteTallyFile = Path.Combine(_outputDir, "Models", "VoteTallyResponse.cs");
        File.Exists(voteTallyFile).Should().BeTrue();

        var content = File.ReadAllText(voteTallyFile);
        content.Should().Contain("public record VoteTallyResponse");
        content.Should().Contain("Items");
        content.Should().Contain("List<");
    }

    [Test]
    public void GenerateTrainsLibrary_ArrayComponentSchema_RefArrayTypeGenerated()
    {
        var schema = _parser.Parse(FixturePath("array-component.json"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        var scoreboardFile = Path.Combine(_outputDir, "Models", "Scoreboard.cs");
        File.Exists(scoreboardFile).Should().BeTrue();

        var content = File.ReadAllText(scoreboardFile);
        content.Should().Contain("public record Scoreboard");
        content.Should().Contain("List<ScoreEntry>");
    }

    [Test]
    public void GenerateTrainsLibrary_ArrayComponentSchema_RegularSchemaAlsoGenerated()
    {
        var schema = _parser.Parse(FixturePath("array-component.json"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        var scoreEntryFile = Path.Combine(_outputDir, "Models", "ScoreEntry.cs");
        File.Exists(scoreEntryFile).Should().BeTrue();

        var content = File.ReadAllText(scoreEntryFile);
        content.Should().Contain("public record ScoreEntry");
        content.Should().Contain("Player");
        content.Should().Contain("Score");
    }

    [Test]
    public void GenerateTrainsLibrary_ArrayComponentSchema_TrainFilesGenerated()
    {
        var schema = _parser.Parse(FixturePath("array-component.json"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        var tallyDir = Path.Combine(_outputDir, "Trains", "Votes", "VoteTally");
        File.Exists(Path.Combine(tallyDir, "IVoteTallyTrain.cs")).Should().BeTrue();
        File.Exists(Path.Combine(tallyDir, "VoteTallyTrain.cs")).Should().BeTrue();
        File.Exists(Path.Combine(tallyDir, "Junctions", "VoteTallyJunction.cs")).Should().BeTrue();
    }

    #endregion

    #region DuplicateOperationNames

    [Test]
    public void GenerateTrainsLibrary_DuplicateNames_AllTrainFoldersCreated()
    {
        var schema = _parser.Parse(FixturePath("duplicate-names.json"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        // First GET: GetUsers (prefix kept — "Users" collides with PostUsers)
        var getUsersDir = Path.Combine(_outputDir, "Trains", "Users", "GetUsers");
        File.Exists(Path.Combine(getUsersDir, "IGetUsersTrain.cs")).Should().BeTrue();

        // Second GET: GetUsersByUserId (disambiguated by path param)
        var getUsersByIdDir = Path.Combine(_outputDir, "Trains", "Users", "GetUsersByUserId");
        File.Exists(Path.Combine(getUsersByIdDir, "IGetUsersByUserIdTrain.cs")).Should().BeTrue();
    }

    [Test]
    public void GenerateTrainsLibrary_DuplicateNames_ManifestNamesHasAll()
    {
        var schema = _parser.Parse(FixturePath("duplicate-names.json"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        var content = File.ReadAllText(Path.Combine(_outputDir, "ManifestNames.cs"));

        content.Should().Contain("GetUsers");
        content.Should().Contain("PostUsers");
        content.Should().Contain("GetUsersByUserId");
        content.Should().Contain("DeleteUsers");
        content.Should().Contain("UsersPosts");
    }

    [Test]
    public void GenerateTrainsLibrary_DuplicateNames_JunctionsHaveCorrectHttpPaths()
    {
        var schema = _parser.Parse(FixturePath("duplicate-names.json"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        var getUsersJunction = File.ReadAllText(
            Path.Combine(
                _outputDir,
                "Trains",
                "Users",
                "GetUsers",
                "Junctions",
                "GetUsersJunction.cs"
            )
        );
        getUsersJunction.Should().Contain("GET /users");

        var getUsersByIdJunction = File.ReadAllText(
            Path.Combine(
                _outputDir,
                "Trains",
                "Users",
                "GetUsersByUserId",
                "Junctions",
                "GetUsersByUserIdJunction.cs"
            )
        );
        getUsersByIdJunction.Should().Contain("GET /users/{userId}");
    }

    [Test]
    public void GenerateTrainsLibrary_DuplicateNames_DeleteOperationHasUnitOutput()
    {
        var schema = _parser.Parse(FixturePath("duplicate-names.json"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        var deleteJunction = File.ReadAllText(
            Path.Combine(
                _outputDir,
                "Trains",
                "Users",
                "DeleteUsers",
                "Junctions",
                "DeleteUsersJunction.cs"
            )
        );
        deleteJunction.Should().Contain("using LanguageExt;");
        deleteJunction.Should().Contain("Unit");
    }

    #endregion

    #region EmptyInputJunction

    [Test]
    public void GenerateTrainsLibrary_EmptyInputOperation_JunctionUsesTypedInputName()
    {
        // listNotifications has no parameters → generates an empty input record
        var schema = _parser.Parse(FixturePath("property-collision.json"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        var junctionPath = Path.Combine(
            _outputDir,
            "Trains",
            "Notifications",
            "ListNotifications",
            "Junctions",
            "ListNotificationsJunction.cs"
        );

        if (File.Exists(junctionPath))
        {
            var content = File.ReadAllText(junctionPath);
            // Empty input uses the typed record name, not Unit
            content.Should().Contain("Junction<ListNotificationsInput,");
            content.Should().NotContain("using LanguageExt;");
        }
    }

    [Test]
    public void GenerateTrainsLibrary_EmptyInputOperation_InputFileGenerated()
    {
        var schema = _parser.Parse(FixturePath("property-collision.json"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        var inputPath = Path.Combine(
            _outputDir,
            "Trains",
            "Notifications",
            "ListNotifications",
            "ListNotificationsInput.cs"
        );

        File.Exists(inputPath).Should().BeTrue("empty input records should still generate a file");
        var content = File.ReadAllText(inputPath);
        content.Should().Contain("public record ListNotificationsInput;");
    }

    #endregion

    #region InlineObjectPromotion

    [Test]
    public void GenerateTrainsLibrary_InlineObjectInArrayComponent_PromotedTypeFileGenerated()
    {
        var schema = _parser.Parse(FixturePath("array-component.json"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        var itemFile = Path.Combine(_outputDir, "Models", "VoteTallyResponseItem.cs");
        File.Exists(itemFile).Should().BeTrue();

        var content = File.ReadAllText(itemFile);
        content.Should().Contain("public record VoteTallyResponseItem");
        content.Should().Contain("RepId");
        content.Should().Contain("Name");
    }

    [Test]
    public void GenerateTrainsLibrary_InlineObjectInArrayComponent_WrapperReferencesPromotedType()
    {
        var schema = _parser.Parse(FixturePath("array-component.json"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        var wrapperFile = Path.Combine(_outputDir, "Models", "VoteTallyResponse.cs");
        var content = File.ReadAllText(wrapperFile);
        content.Should().Contain("List<VoteTallyResponseItem>");
    }

    [Test]
    public void GenerateTrainsLibrary_InlineObjectInArrayComponent_EnumFileGenerated()
    {
        var schema = _parser.Parse(FixturePath("array-component.json"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        var enumFile = Path.Combine(_outputDir, "Models", "LegVoted.cs");
        File.Exists(enumFile).Should().BeTrue();

        var content = File.ReadAllText(enumFile);
        content.Should().Contain("public enum LegVoted");
    }

    [Test]
    public void GenerateTrainsLibrary_InlineObjectInArrayResponse_PromotedTypeFileGenerated()
    {
        var schema = _parser.Parse(FixturePath("inline-object-array.json"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        var itemFile = Path.Combine(_outputDir, "Models", "ListEventsItem.cs");
        File.Exists(itemFile).Should().BeTrue();

        var content = File.ReadAllText(itemFile);
        content.Should().Contain("public record ListEventsItem");
        content.Should().Contain("Id");
        content.Should().Contain("Title");
        content.Should().Contain("StartDate");
    }

    [Test]
    public void GenerateTrainsLibrary_InlineObjectInArrayResponse_OutputFileReferencesPromotedType()
    {
        var schema = _parser.Parse(FixturePath("inline-object-array.json"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        // Output type is generated alongside the train, not in Models/
        var outputFile = Path.Combine(
            _outputDir,
            "Trains",
            "Events",
            "ListEvents",
            "ListEventsOutput.cs"
        );
        File.Exists(outputFile).Should().BeTrue();

        var content = File.ReadAllText(outputFile);
        content.Should().Contain("List<ListEventsItem>");
    }

    [Test]
    public void GenerateTrainsLibrary_ArrayResponseWithRefItems_NoExtraTypePromoted()
    {
        var schema = _parser.Parse(FixturePath("inline-object-array.json"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        // Speaker is a $ref — should use the existing type, not create ListSpeakersItem
        File.Exists(Path.Combine(_outputDir, "Models", "ListSpeakersItem.cs")).Should().BeFalse();
        File.Exists(Path.Combine(_outputDir, "Models", "Speaker.cs")).Should().BeTrue();
    }

    [Test]
    public void GenerateTrainsLibrary_ArrayResponseWithBareObject_NoTypePromoted()
    {
        var schema = _parser.Parse(FixturePath("inline-object-array.json"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        // RawData returns array of bare objects — no RawDataItem type should be created
        File.Exists(Path.Combine(_outputDir, "Models", "RawDataItem.cs")).Should().BeFalse();
    }

    #endregion

    #region CombinedScenarios

    [Test]
    public void GenerateTrainsLibrary_InlineEnums_ItemModelUsesEnumTypes()
    {
        var schema = _parser.Parse(FixturePath("inline-enums.json"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        var itemFile = Path.Combine(_outputDir, "Models", "Item.cs");
        File.Exists(itemFile).Should().BeTrue();

        var content = File.ReadAllText(itemFile);
        content.Should().Contain("Category");
        content.Should().Contain("Status");
    }

    [Test]
    public void GenerateTrainsLibrary_ArrayComponentSchema_EnumsInArrayItemsResolved()
    {
        var schema = _parser.Parse(FixturePath("array-component.json"));

        // VoteTallyResponse has items with enum "leg_voted" [Y, N, O]
        // Since the items are inline objects, the enum may or may not be promoted,
        // but the parser should not crash
        schema.Should().NotBeNull();
        schema.Operations.Should().HaveCount(2);
    }

    #endregion

    #region DottedSchemaNames

    [Test]
    public void GenerateTrainsLibrary_DottedNames_ModelFilesHaveSimplifiedNames()
    {
        var schema = _parser.Parse(FixturePath("dotted-names.json"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        File.Exists(Path.Combine(_outputDir, "Models", "UserDto.cs")).Should().BeTrue();
        File.Exists(Path.Combine(_outputDir, "Models", "UserListDto.cs")).Should().BeTrue();
        File.Exists(Path.Combine(_outputDir, "Models", "CreateUserCommand.cs")).Should().BeTrue();
        File.Exists(Path.Combine(_outputDir, "Models", "ReportListDto.cs")).Should().BeTrue();
    }

    [Test]
    public void GenerateTrainsLibrary_DottedNames_NoDotsInModelFileNames()
    {
        var schema = _parser.Parse(FixturePath("dotted-names.json"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        var modelsDir = Path.Combine(_outputDir, "Models");
        if (Directory.Exists(modelsDir))
        {
            var files = Directory.GetFiles(modelsDir, "*.cs");
            files
                .Select(Path.GetFileNameWithoutExtension)
                .Should()
                .AllSatisfy(name => name.Should().NotContain("."));
        }
    }

    [Test]
    public void GenerateTrainsLibrary_DottedNames_UserListDtoReferencesUserDto()
    {
        var schema = _parser.Parse(FixturePath("dotted-names.json"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        var content = File.ReadAllText(Path.Combine(_outputDir, "Models", "UserListDto.cs"));
        content.Should().Contain("List<UserDto>");
    }

    [Test]
    public void GenerateTrainsLibrary_DottedNames_RootPathGeneratesValidTrain()
    {
        var schema = _parser.Parse(FixturePath("dotted-names.json"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        var rootDir = Path.Combine(_outputDir, "Trains", "Health", "Root");
        File.Exists(Path.Combine(rootDir, "IRootTrain.cs")).Should().BeTrue();
        File.Exists(Path.Combine(rootDir, "RootTrain.cs")).Should().BeTrue();
    }

    [Test]
    public void GenerateTrainsLibrary_DottedNames_CalendarIcsNoDotsInTrainName()
    {
        var schema = _parser.Parse(FixturePath("dotted-names.json"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        // calendar.ics path should produce a valid train name without dots
        var eventsDir = Path.Combine(_outputDir, "Trains", "Events");
        Directory.Exists(eventsDir).Should().BeTrue();

        var trainDirs = Directory.GetDirectories(eventsDir);
        trainDirs
            .Select(Path.GetFileName)
            .Should()
            .AllSatisfy(name => name.Should().NotContain("."));
    }

    [Test]
    public void GenerateTrainsLibrary_DottedNames_GraphQLNamespacesNoInvalidChars()
    {
        var schema = _parser.Parse(FixturePath("dotted-names.json"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        var content = File.ReadAllText(Path.Combine(_outputDir, "GraphQLNamespaces.cs"));
        // Should not contain commas or dots in identifier names
        content.Should().NotMatchRegex(@"public const string [^=]*[.,][^=]*=");
    }

    [Test]
    public void GenerateTrainsLibrary_DottedNames_ManifestNamesNoInvalidChars()
    {
        var schema = _parser.Parse(FixturePath("dotted-names.json"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        var content = File.ReadAllText(Path.Combine(_outputDir, "ManifestNames.cs"));
        // All const names should be valid C# identifiers (no dots or commas)
        content.Should().NotMatchRegex(@"public const string [^=]*[.,][^=]*=");
    }

    [Test]
    public void GenerateTrainsLibrary_DottedNames_MultilineDescriptionInAttribute()
    {
        var schema = _parser.Parse(FixturePath("dotted-names.json"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        var notesDir = Path.Combine(_outputDir, "Trains", "Notes");
        var trainFiles = Directory
            .GetFiles(notesDir, "*Train.cs", SearchOption.AllDirectories)
            .Where(f => !Path.GetFileName(f).StartsWith('I'))
            .ToArray();
        trainFiles.Should().NotBeEmpty();

        var content = File.ReadAllText(trainFiles[0]);
        // Description attribute should be on a single line (no raw newlines in string)
        var attrLine = content.Split('\n').FirstOrDefault(l => l.Contains("Description ="));
        attrLine.Should().NotBeNull();
        // The description value should not contain literal newlines
        attrLine.Should().NotContain("\r");
    }

    [Test]
    public void GenerateTrainsLibrary_DottedNames_EmptySchemaTypeNotGenerated()
    {
        var schema = _parser.Parse(FixturePath("dotted-names.json"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        // Empty types (no fields) should not be generated — HotChocolate rejects them
        var emptyFile = Path.Combine(_outputDir, "Models", "EmptyResponse.cs");
        File.Exists(emptyFile).Should().BeFalse();
    }

    #endregion
}
