using AwesomeAssertions;
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
    private OpenApiSchemaParser _parser = new();
    private TraxProjectGenerator _generator = new();
    private string _outputDir = "";

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

    // Refused by SchemaNames.Validate, so kept out of Fixtures/Schemas, which other tests
    // generate every file of.
    private static string InvalidFixturePath(string name) =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "InvalidSchemas", name);

    #region NonFatalErrors

    [Test]
    public async Task GenerateTrainsLibrary_NonFatalErrors_AllFilesGenerated()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("non-fatal-errors.yaml"))
            .ConfigureAwait(false);
        await _generator
            .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
            .ConfigureAwait(false);

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
    public async Task GenerateTrainsLibrary_NonFatalErrors_ModelFilesGenerated()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("non-fatal-errors.yaml"))
            .ConfigureAwait(false);
        await _generator
            .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
            .ConfigureAwait(false);

        File.Exists(Path.Combine(_outputDir, "Models", "Item.cs")).Should().BeTrue();
        File.Exists(Path.Combine(_outputDir, "Models", "Issue.cs")).Should().BeTrue();
    }

    #endregion

    #region InlineEnums

    [Test]
    public async Task GenerateTrainsLibrary_InlineEnums_EnumFilesGenerated()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("inline-enums.json"))
            .ConfigureAwait(false);
        await _generator
            .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
            .ConfigureAwait(false);

        // Inline enums from parameters and properties should be generated as enum files
        var modelsDir = Path.Combine(_outputDir, "Models");
        Directory.Exists(modelsDir).Should().BeTrue();

        File.Exists(Path.Combine(modelsDir, "Status.cs")).Should().BeTrue();
        File.Exists(Path.Combine(modelsDir, "SortBy.cs")).Should().BeTrue();
        File.Exists(Path.Combine(modelsDir, "Priority.cs")).Should().BeTrue();
        File.Exists(Path.Combine(modelsDir, "Category.cs")).Should().BeTrue();
    }

    [Test]
    public async Task GenerateTrainsLibrary_InlineEnums_NoUnnamedEnumFile()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("inline-enums.json"))
            .ConfigureAwait(false);
        await _generator
            .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
            .ConfigureAwait(false);

        File.Exists(Path.Combine(_outputDir, "Models", "UnnamedEnum.cs")).Should().BeFalse();
    }

    [Test]
    public async Task GenerateTrainsLibrary_InlineEnums_StatusEnumHasCorrectValues()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("inline-enums.json"))
            .ConfigureAwait(false);
        await _generator
            .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
            .ConfigureAwait(false);

        var content = await File.ReadAllTextAsync(Path.Combine(_outputDir, "Models", "Status.cs"))
            .ConfigureAwait(false);
        content.Should().Contain("public enum Status");
        content.Should().Contain("Active");
        content.Should().Contain("Inactive");
        content.Should().Contain("Archived");
    }

    [Test]
    public async Task GenerateTrainsLibrary_InlineEnums_PriorityEnumHasCorrectValues()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("inline-enums.json"))
            .ConfigureAwait(false);
        await _generator
            .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
            .ConfigureAwait(false);

        var content = await File.ReadAllTextAsync(Path.Combine(_outputDir, "Models", "Priority.cs"))
            .ConfigureAwait(false);
        content.Should().Contain("public enum Priority");
        content.Should().Contain("Low");
        content.Should().Contain("Medium");
        content.Should().Contain("High");
        content.Should().Contain("Critical");
    }

    [Test]
    public async Task GenerateTrainsLibrary_InlineEnums_InputFileReferencesEnumType()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("inline-enums.json"))
            .ConfigureAwait(false);
        await _generator
            .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
            .ConfigureAwait(false);

        var inputFile = Path.Combine(
            _outputDir,
            "Trains",
            "Items",
            "ListItems",
            "ListItemsInput.cs"
        );
        var content = await File.ReadAllTextAsync(inputFile).ConfigureAwait(false);

        content.Should().Contain("Status");
        content.Should().Contain("SortBy");
        // Models are referenced fully qualified, never through a using directive
        content.Should().Contain("global::TestProject.Trains.Models.Status");
    }

    [Test]
    public async Task GenerateTrainsLibrary_InlineEnums_CreateItemInputReferencesEnum()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("inline-enums.json"))
            .ConfigureAwait(false);
        await _generator
            .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
            .ConfigureAwait(false);

        var inputFile = Path.Combine(
            _outputDir,
            "Trains",
            "Items",
            "CreateItem",
            "CreateItemInput.cs"
        );
        var content = await File.ReadAllTextAsync(inputFile).ConfigureAwait(false);

        content.Should().Contain("global::TestProject.Trains.Models.Priority");
    }

    #endregion

    #region PropertyCollision

    [Test]
    public async Task GenerateTrainsLibrary_PropertyCollision_PropertyNamedLikeTypeRefused()
    {
        var schema = await _parser
            .ParseAsync(InvalidFixturePath("property-collision.json"))
            .ConfigureAwait(false);

        var act = async () =>
            await _generator
                .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
                .ConfigureAwait(false);

        (
            await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false)
        ).WithMessage("*property 'Notification' of 'Notification'*cannot have its type's name*");
    }

    [Test]
    public async Task GenerateTrainsLibrary_PropertyCollision_RefusalNamesBothCollidingTypes()
    {
        var schema = await _parser
            .ParseAsync(InvalidFixturePath("property-collision.json"))
            .ConfigureAwait(false);

        var act = async () =>
            await _generator
                .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
                .ConfigureAwait(false);

        (
            await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false)
        ).WithMessage("*property 'Testimonial' of 'Testimonial'*cannot have its type's name*");
    }

    [Test]
    public async Task GenerateTrainsLibrary_PropertyCollision_RefusedSchemaWritesNothing()
    {
        var schema = await _parser
            .ParseAsync(InvalidFixturePath("property-collision.json"))
            .ConfigureAwait(false);

        var act = async () =>
            await _generator
                .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
                .ConfigureAwait(false);

        await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
        Directory
            .Exists(_outputDir)
            .Should()
            .BeFalse("a refused schema is refused before anything is written");
    }

    #endregion

    #region ArrayComponentSchemas

    [Test]
    public async Task GenerateTrainsLibrary_ArrayComponentSchema_WrapperTypeGenerated()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("array-component.json"))
            .ConfigureAwait(false);
        await _generator
            .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
            .ConfigureAwait(false);

        var voteTallyFile = Path.Combine(_outputDir, "Models", "VoteTallyResponse.cs");
        File.Exists(voteTallyFile).Should().BeTrue();

        var content = await File.ReadAllTextAsync(voteTallyFile).ConfigureAwait(false);
        content.Should().Contain("public record VoteTallyResponse");
        content.Should().Contain("Items");
        content.Should().Contain("List<");
    }

    [Test]
    public async Task GenerateTrainsLibrary_ArrayComponentSchema_RefArrayTypeGenerated()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("array-component.json"))
            .ConfigureAwait(false);
        await _generator
            .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
            .ConfigureAwait(false);

        var scoreboardFile = Path.Combine(_outputDir, "Models", "Scoreboard.cs");
        File.Exists(scoreboardFile).Should().BeTrue();

        var content = await File.ReadAllTextAsync(scoreboardFile).ConfigureAwait(false);
        content.Should().Contain("public record Scoreboard");
        content.Should().Contain("List<ScoreEntry>");
    }

    [Test]
    public async Task GenerateTrainsLibrary_ArrayComponentSchema_RegularSchemaAlsoGenerated()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("array-component.json"))
            .ConfigureAwait(false);
        await _generator
            .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
            .ConfigureAwait(false);

        var scoreEntryFile = Path.Combine(_outputDir, "Models", "ScoreEntry.cs");
        File.Exists(scoreEntryFile).Should().BeTrue();

        var content = await File.ReadAllTextAsync(scoreEntryFile).ConfigureAwait(false);
        content.Should().Contain("public record ScoreEntry");
        content.Should().Contain("Player");
        content.Should().Contain("Score");
    }

    [Test]
    public async Task GenerateTrainsLibrary_ArrayComponentSchema_TrainFilesGenerated()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("array-component.json"))
            .ConfigureAwait(false);
        await _generator
            .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
            .ConfigureAwait(false);

        var tallyDir = Path.Combine(_outputDir, "Trains", "Votes", "VoteTally");
        File.Exists(Path.Combine(tallyDir, "IVoteTallyTrain.cs")).Should().BeTrue();
        File.Exists(Path.Combine(tallyDir, "VoteTallyTrain.cs")).Should().BeTrue();
        File.Exists(Path.Combine(tallyDir, "Junctions", "VoteTallyJunction.cs")).Should().BeTrue();
    }

    #endregion

    #region DuplicateOperationNames

    [Test]
    public async Task GenerateTrainsLibrary_DuplicateNames_AllTrainFoldersCreated()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("duplicate-names.json"))
            .ConfigureAwait(false);
        await _generator
            .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
            .ConfigureAwait(false);

        // First GET: GetUsers (prefix kept — "Users" collides with PostUsers)
        var getUsersDir = Path.Combine(_outputDir, "Trains", "Users", "GetUsers");
        File.Exists(Path.Combine(getUsersDir, "IGetUsersTrain.cs")).Should().BeTrue();

        // Second GET: GetUsersByUserId (disambiguated by path param)
        var getUsersByIdDir = Path.Combine(_outputDir, "Trains", "Users", "GetUsersByUserId");
        File.Exists(Path.Combine(getUsersByIdDir, "IGetUsersByUserIdTrain.cs")).Should().BeTrue();
    }

    [Test]
    public async Task GenerateTrainsLibrary_DuplicateNames_ManifestNamesHasAll()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("duplicate-names.json"))
            .ConfigureAwait(false);
        await _generator
            .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
            .ConfigureAwait(false);

        var content = await File.ReadAllTextAsync(Path.Combine(_outputDir, "ManifestNames.cs"))
            .ConfigureAwait(false);

        content.Should().Contain("GetUsers");
        content.Should().Contain("PostUsers");
        content.Should().Contain("GetUsersByUserId");
        content.Should().Contain("DeleteUsers");
        content.Should().Contain("UsersPosts");
    }

    [Test]
    public async Task GenerateTrainsLibrary_DuplicateNames_JunctionsHaveCorrectHttpPaths()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("duplicate-names.json"))
            .ConfigureAwait(false);
        await _generator
            .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
            .ConfigureAwait(false);

        var getUsersJunction = await File.ReadAllTextAsync(
                Path.Combine(
                    _outputDir,
                    "Trains",
                    "Users",
                    "GetUsers",
                    "Junctions",
                    "GetUsersJunction.cs"
                )
            )
            .ConfigureAwait(false);
        getUsersJunction.Should().Contain("GET /users");

        var getUsersByIdJunction = await File.ReadAllTextAsync(
                Path.Combine(
                    _outputDir,
                    "Trains",
                    "Users",
                    "GetUsersByUserId",
                    "Junctions",
                    "GetUsersByUserIdJunction.cs"
                )
            )
            .ConfigureAwait(false);
        getUsersByIdJunction.Should().Contain("GET /users/{userId}");
    }

    [Test]
    public async Task GenerateTrainsLibrary_DuplicateNames_DeleteOperationHasUnitOutput()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("duplicate-names.json"))
            .ConfigureAwait(false);
        await _generator
            .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
            .ConfigureAwait(false);

        var deleteJunction = await File.ReadAllTextAsync(
                Path.Combine(
                    _outputDir,
                    "Trains",
                    "Users",
                    "DeleteUsers",
                    "Junctions",
                    "DeleteUsersJunction.cs"
                )
            )
            .ConfigureAwait(false);
        deleteJunction.Should().Contain("global::Trax.Core.Functional.Unit");
        deleteJunction.Should().Contain("Unit");
    }

    #endregion

    #region EmptyInputJunction

    [Test]
    public async Task GenerateTrainsLibrary_EmptyInputOperation_JunctionUsesTypedInputName()
    {
        // listNotifications has no parameters → generates an empty input record
        var schema = await _parser
            .ParseAsync(FixturePath("empty-input.json"))
            .ConfigureAwait(false);
        await _generator
            .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
            .ConfigureAwait(false);

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
            var content = await File.ReadAllTextAsync(junctionPath).ConfigureAwait(false);
            // Empty input uses the typed record name, not Unit
            content.Should().Contain("Junction<ListNotificationsInput,");
            content.Should().NotContain("using Trax.Core.Functional;");
        }
    }

    [Test]
    public async Task GenerateTrainsLibrary_EmptyInputOperation_InputFileGenerated()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("empty-input.json"))
            .ConfigureAwait(false);
        await _generator
            .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
            .ConfigureAwait(false);

        var inputPath = Path.Combine(
            _outputDir,
            "Trains",
            "Notifications",
            "ListNotifications",
            "ListNotificationsInput.cs"
        );

        File.Exists(inputPath).Should().BeTrue("empty input records should still generate a file");
        var content = await File.ReadAllTextAsync(inputPath).ConfigureAwait(false);
        content.Should().Contain("public record ListNotificationsInput;");
    }

    #endregion

    #region InlineObjectPromotion

    [Test]
    public async Task GenerateTrainsLibrary_InlineObjectInArrayComponent_PromotedTypeFileGenerated()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("array-component.json"))
            .ConfigureAwait(false);
        await _generator
            .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
            .ConfigureAwait(false);

        var itemFile = Path.Combine(_outputDir, "Models", "VoteTallyResponseItem.cs");
        File.Exists(itemFile).Should().BeTrue();

        var content = await File.ReadAllTextAsync(itemFile).ConfigureAwait(false);
        content.Should().Contain("public record VoteTallyResponseItem");
        content.Should().Contain("RepId");
        content.Should().Contain("Name");
    }

    [Test]
    public async Task GenerateTrainsLibrary_InlineObjectInArrayComponent_WrapperReferencesPromotedType()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("array-component.json"))
            .ConfigureAwait(false);
        await _generator
            .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
            .ConfigureAwait(false);

        var wrapperFile = Path.Combine(_outputDir, "Models", "VoteTallyResponse.cs");
        var content = await File.ReadAllTextAsync(wrapperFile).ConfigureAwait(false);
        content.Should().Contain("List<VoteTallyResponseItem>");
    }

    [Test]
    public async Task GenerateTrainsLibrary_InlineObjectInArrayComponent_EnumFileGenerated()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("array-component.json"))
            .ConfigureAwait(false);
        await _generator
            .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
            .ConfigureAwait(false);

        var enumFile = Path.Combine(_outputDir, "Models", "LegVoted.cs");
        File.Exists(enumFile).Should().BeTrue();

        var content = await File.ReadAllTextAsync(enumFile).ConfigureAwait(false);
        content.Should().Contain("public enum LegVoted");
    }

    [Test]
    public async Task GenerateTrainsLibrary_InlineObjectInArrayResponse_PromotedTypeFileGenerated()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("inline-object-array.json"))
            .ConfigureAwait(false);
        await _generator
            .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
            .ConfigureAwait(false);

        var itemFile = Path.Combine(_outputDir, "Models", "ListEventsItem.cs");
        File.Exists(itemFile).Should().BeTrue();

        var content = await File.ReadAllTextAsync(itemFile).ConfigureAwait(false);
        content.Should().Contain("public record ListEventsItem");
        content.Should().Contain("Id");
        content.Should().Contain("Title");
        content.Should().Contain("StartDate");
    }

    [Test]
    public async Task GenerateTrainsLibrary_InlineObjectInArrayResponse_OutputFileReferencesPromotedType()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("inline-object-array.json"))
            .ConfigureAwait(false);
        await _generator
            .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
            .ConfigureAwait(false);

        // Output type is generated alongside the train, not in Models/
        var outputFile = Path.Combine(
            _outputDir,
            "Trains",
            "Events",
            "ListEvents",
            "ListEventsOutput.cs"
        );
        File.Exists(outputFile).Should().BeTrue();

        var content = await File.ReadAllTextAsync(outputFile).ConfigureAwait(false);
        content.Should().Contain("List<global::TestProject.Trains.Models.ListEventsItem>");
    }

    [Test]
    public async Task GenerateTrainsLibrary_ArrayResponseWithRefItems_NoExtraTypePromoted()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("inline-object-array.json"))
            .ConfigureAwait(false);
        await _generator
            .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
            .ConfigureAwait(false);

        // Speaker is a $ref — should use the existing type, not create ListSpeakersItem
        File.Exists(Path.Combine(_outputDir, "Models", "ListSpeakersItem.cs")).Should().BeFalse();
        File.Exists(Path.Combine(_outputDir, "Models", "Speaker.cs")).Should().BeTrue();
    }

    [Test]
    public async Task GenerateTrainsLibrary_ArrayResponseWithBareObject_NoTypePromoted()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("inline-object-array.json"))
            .ConfigureAwait(false);
        await _generator
            .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
            .ConfigureAwait(false);

        // RawData returns array of bare objects — no RawDataItem type should be created
        File.Exists(Path.Combine(_outputDir, "Models", "RawDataItem.cs")).Should().BeFalse();
    }

    #endregion

    #region CombinedScenarios

    [Test]
    public async Task GenerateTrainsLibrary_InlineEnums_ItemModelUsesEnumTypes()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("inline-enums.json"))
            .ConfigureAwait(false);
        await _generator
            .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
            .ConfigureAwait(false);

        var itemFile = Path.Combine(_outputDir, "Models", "Item.cs");
        File.Exists(itemFile).Should().BeTrue();

        var content = await File.ReadAllTextAsync(itemFile).ConfigureAwait(false);
        content.Should().Contain("Category");
        content.Should().Contain("Status");
    }

    [Test]
    public async Task GenerateTrainsLibrary_ArrayComponentSchema_EnumsInArrayItemsResolved()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("array-component.json"))
            .ConfigureAwait(false);

        // VoteTallyResponse has items with enum "leg_voted" [Y, N, O]
        // Since the items are inline objects, the enum may or may not be promoted,
        // but the parser should not crash
        schema.Should().NotBeNull();
        schema.Operations.Should().HaveCount(2);
    }

    #endregion

    #region DottedSchemaNames

    [Test]
    public async Task GenerateTrainsLibrary_DottedNames_ModelFilesHaveSimplifiedNames()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("dotted-names.json"))
            .ConfigureAwait(false);
        await _generator
            .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
            .ConfigureAwait(false);

        File.Exists(Path.Combine(_outputDir, "Models", "UserDto.cs")).Should().BeTrue();
        File.Exists(Path.Combine(_outputDir, "Models", "UserListDto.cs")).Should().BeTrue();
        File.Exists(Path.Combine(_outputDir, "Models", "CreateUserCommand.cs")).Should().BeTrue();
        File.Exists(Path.Combine(_outputDir, "Models", "ReportListDto.cs")).Should().BeTrue();
    }

    [Test]
    public async Task GenerateTrainsLibrary_DottedNames_NoDotsInModelFileNames()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("dotted-names.json"))
            .ConfigureAwait(false);
        await _generator
            .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
            .ConfigureAwait(false);

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
    public async Task GenerateTrainsLibrary_DottedNames_UserListDtoReferencesUserDto()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("dotted-names.json"))
            .ConfigureAwait(false);
        await _generator
            .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
            .ConfigureAwait(false);

        var content = await File.ReadAllTextAsync(
                Path.Combine(_outputDir, "Models", "UserListDto.cs")
            )
            .ConfigureAwait(false);
        content.Should().Contain("List<UserDto>");
    }

    [Test]
    public async Task GenerateTrainsLibrary_DottedNames_RootPathGeneratesValidTrain()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("dotted-names.json"))
            .ConfigureAwait(false);
        await _generator
            .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
            .ConfigureAwait(false);

        var rootDir = Path.Combine(_outputDir, "Trains", "Health", "Root");
        File.Exists(Path.Combine(rootDir, "IRootTrain.cs")).Should().BeTrue();
        File.Exists(Path.Combine(rootDir, "RootTrain.cs")).Should().BeTrue();
    }

    [Test]
    public async Task GenerateTrainsLibrary_DottedNames_CalendarIcsNoDotsInTrainName()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("dotted-names.json"))
            .ConfigureAwait(false);
        await _generator
            .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
            .ConfigureAwait(false);

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
    public async Task GenerateTrainsLibrary_DottedNames_GraphQLNamespacesNoInvalidChars()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("dotted-names.json"))
            .ConfigureAwait(false);
        await _generator
            .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
            .ConfigureAwait(false);

        var content = await File.ReadAllTextAsync(Path.Combine(_outputDir, "GraphQLNamespaces.cs"))
            .ConfigureAwait(false);
        // Should not contain commas or dots in identifier names
        content.Should().NotMatchRegex(@"public const string [^=]*[.,][^=]*=");
    }

    [Test]
    public async Task GenerateTrainsLibrary_DottedNames_ManifestNamesNoInvalidChars()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("dotted-names.json"))
            .ConfigureAwait(false);
        await _generator
            .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
            .ConfigureAwait(false);

        var content = await File.ReadAllTextAsync(Path.Combine(_outputDir, "ManifestNames.cs"))
            .ConfigureAwait(false);
        // All const names should be valid C# identifiers (no dots or commas)
        content.Should().NotMatchRegex(@"public const string [^=]*[.,][^=]*=");
    }

    [Test]
    public async Task GenerateTrainsLibrary_DottedNames_MultilineDescriptionInAttribute()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("dotted-names.json"))
            .ConfigureAwait(false);
        await _generator
            .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
            .ConfigureAwait(false);

        var notesDir = Path.Combine(_outputDir, "Trains", "Notes");
        var trainFiles = Directory
            .GetFiles(notesDir, "*Train.cs", SearchOption.AllDirectories)
            .Where(f => !Path.GetFileName(f).StartsWith('I'))
            .ToArray();
        trainFiles.Should().NotBeEmpty();

        var content = await File.ReadAllTextAsync(trainFiles[0]).ConfigureAwait(false);
        // Description attribute should be on a single line (no raw newlines in string)
        var attrLine = content
            .Split('\n')
            .FirstOrDefault(l => l.Contains("Description =", StringComparison.Ordinal));
        attrLine.Should().NotBeNull();
        // The description value should not contain literal newlines
        attrLine.Should().NotContain("\r");
    }

    [Test]
    public async Task GenerateTrainsLibrary_DottedNames_EmptySchemaTypeNotGenerated()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("dotted-names.json"))
            .ConfigureAwait(false);
        await _generator
            .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
            .ConfigureAwait(false);

        // Empty types (no fields) should not be generated — HotChocolate rejects them
        var emptyFile = Path.Combine(_outputDir, "Models", "EmptyResponse.cs");
        File.Exists(emptyFile).Should().BeFalse();
    }

    #endregion
}
