using AwesomeAssertions;
using Trax.Cli.Models;
using Trax.Cli.Schema.OpenApi;
using Trax.Core.Extensions;

namespace Trax.Cli.Tests.UnitTests;

public class OpenApiSchemaParserTests
{
    private static string FixturePath(string name) =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Schemas", name);

    #region Petstore

    [Test]
    public async Task Parse_Petstore_Returns4Operations()
    {
        var parser = new OpenApiSchemaParser();

        var schema = await parser.ParseAsync(FixturePath("petstore.json")).ConfigureAwait(false);

        schema.Operations.Should().HaveCount(4);
        schema
            .Operations.Select(o => o.Name)
            .Should()
            .BeEquivalentTo("ListPets", "CreatePet", "GetPet", "DeletePet");
    }

    [Test]
    public async Task Parse_Petstore_GetOperationsAreQuery()
    {
        var parser = new OpenApiSchemaParser();

        var schema = await parser.ParseAsync(FixturePath("petstore.json")).ConfigureAwait(false);

        schema
            .Operations.Where(o => o.Name is "ListPets" or "GetPet")
            .Should()
            .AllSatisfy(o => o.Kind.Should().Be(OperationKind.Query));
    }

    [Test]
    public async Task Parse_Petstore_PostAndDeleteAreMutation()
    {
        var parser = new OpenApiSchemaParser();

        var schema = await parser.ParseAsync(FixturePath("petstore.json")).ConfigureAwait(false);

        schema
            .Operations.Where(o => o.Name is "CreatePet" or "DeletePet")
            .Should()
            .AllSatisfy(o => o.Kind.Should().Be(OperationKind.Mutation));
    }

    [Test]
    public async Task Parse_Petstore_GetPetInputHasPetIdOfTypeGuid()
    {
        var parser = new OpenApiSchemaParser();

        var schema = await parser.ParseAsync(FixturePath("petstore.json")).ConfigureAwait(false);

        var getPet = schema.Operations.Single(o =>
            string.Equals(o.Name, "GetPet", StringComparison.Ordinal)
        );
        var petIdField = getPet.InputType.Fields.Single(f =>
            string.Equals(f.Name, "PetId", StringComparison.Ordinal)
        );
        petIdField.TypeName.Should().Be("Guid");
    }

    [Test]
    public async Task Parse_Petstore_DeletePetOutputIsUnit()
    {
        var parser = new OpenApiSchemaParser();

        var schema = await parser.ParseAsync(FixturePath("petstore.json")).ConfigureAwait(false);

        var deletePet = schema.Operations.Single(o =>
            string.Equals(o.Name, "DeletePet", StringComparison.Ordinal)
        );
        deletePet.OutputType.Name.Should().Be("Unit");
    }

    [Test]
    public async Task Parse_Petstore_CreatePetInputHasFieldsFromRequest()
    {
        var parser = new OpenApiSchemaParser();

        var schema = await parser.ParseAsync(FixturePath("petstore.json")).ConfigureAwait(false);

        var createPet = schema.Operations.Single(o =>
            string.Equals(o.Name, "CreatePet", StringComparison.Ordinal)
        );
        var nameField = createPet.InputType.Fields.Single(f =>
            string.Equals(f.Name, "Name", StringComparison.Ordinal)
        );
        var tagField = createPet.InputType.Fields.Single(f =>
            string.Equals(f.Name, "Tag", StringComparison.Ordinal)
        );

        nameField.IsRequired.Should().BeTrue();
        tagField.IsRequired.Should().BeFalse();
    }

    [Test]
    public async Task Parse_Petstore_AllOperationsGroupedAsPets()
    {
        var parser = new OpenApiSchemaParser();

        var schema = await parser.ParseAsync(FixturePath("petstore.json")).ConfigureAwait(false);

        schema.Operations.Should().AllSatisfy(o => o.Group.Should().Be("Pets"));
    }

    [Test]
    public async Task Parse_Petstore_OperationsHaveHttpMethodAndPath()
    {
        var parser = new OpenApiSchemaParser();

        var schema = await parser.ParseAsync(FixturePath("petstore.json")).ConfigureAwait(false);

        schema
            .Operations.Should()
            .AllSatisfy(o =>
            {
                o.HttpMethod.Should().NotBeNullOrWhiteSpace();
                o.HttpPath.Should().NotBeNullOrWhiteSpace();
            });
    }

    #endregion

    #region ComplexOpenApi

    [Test]
    public async Task Parse_ComplexOpenApi_Returns4Operations()
    {
        var parser = new OpenApiSchemaParser();

        var schema = await parser
            .ParseAsync(FixturePath("complex-openapi.json"))
            .ConfigureAwait(false);

        schema.Operations.Should().HaveCount(4);
    }

    [Test]
    public async Task Parse_ComplexOpenApi_ListUsersIsQuery_CreateUserIsMutation()
    {
        var parser = new OpenApiSchemaParser();

        var schema = await parser
            .ParseAsync(FixturePath("complex-openapi.json"))
            .ConfigureAwait(false);

        schema
            .Operations.Single(o => string.Equals(o.Name, "ListUsers", StringComparison.Ordinal))
            .Kind.Should()
            .Be(OperationKind.Query);
        schema
            .Operations.Single(o => string.Equals(o.Name, "CreateUser", StringComparison.Ordinal))
            .Kind.Should()
            .Be(OperationKind.Mutation);
    }

    [Test]
    public async Task Parse_ComplexOpenApi_GetUserInputHasUserIdOfTypeLong()
    {
        var parser = new OpenApiSchemaParser();

        var schema = await parser
            .ParseAsync(FixturePath("complex-openapi.json"))
            .ConfigureAwait(false);

        var getUser = schema.Operations.Single(o =>
            string.Equals(o.Name, "GetUser", StringComparison.Ordinal)
        );
        var userIdField = getUser.InputType.Fields.Single(f =>
            string.Equals(f.Name, "UserId", StringComparison.Ordinal)
        );
        userIdField.TypeName.Should().Be("long");
    }

    [Test]
    public async Task Parse_ComplexOpenApi_CreateUserInputHasRequiredAndDateTimeFields()
    {
        var parser = new OpenApiSchemaParser();

        var schema = await parser
            .ParseAsync(FixturePath("complex-openapi.json"))
            .ConfigureAwait(false);

        var createUser = schema.Operations.Single(o =>
            string.Equals(o.Name, "CreateUser", StringComparison.Ordinal)
        );
        var emailField = createUser.InputType.Fields.Single(f =>
            string.Equals(f.Name, "Email", StringComparison.Ordinal)
        );
        var nameField = createUser.InputType.Fields.Single(f =>
            string.Equals(f.Name, "Name", StringComparison.Ordinal)
        );
        var birthDateField = createUser.InputType.Fields.Single(f =>
            string.Equals(f.Name, "BirthDate", StringComparison.Ordinal)
        );

        emailField.IsRequired.Should().BeTrue();
        nameField.IsRequired.Should().BeTrue();
        birthDateField.TypeName.Should().Be("DateTime");
    }

    [Test]
    public async Task Parse_ComplexOpenApi_HealthCheckGetsASynthesizedName()
    {
        var parser = new OpenApiSchemaParser();

        var schema = await parser
            .ParseAsync(FixturePath("complex-openapi.json"))
            .ConfigureAwait(false);

        var healthOp = schema.Operations.SingleOrDefault(o =>
            string.Equals(o.HttpPath, "/health", StringComparison.Ordinal)
        );
        healthOp.Should().NotBeNull();
        healthOp.NotNull().Name.Should().NotBeNullOrWhiteSpace();
        // No operationId in the schema, so the name is synthesized from path segments
        healthOp.Name.Should().Be("Health");
    }

    [Test]
    public async Task Parse_ComplexOpenApi_UserSchemaHasRolesAsListOfString()
    {
        var parser = new OpenApiSchemaParser();

        var schema = await parser
            .ParseAsync(FixturePath("complex-openapi.json"))
            .ConfigureAwait(false);

        var userType = schema.Types.Single(t =>
            string.Equals(t.Name, "User", StringComparison.Ordinal)
        );
        var rolesField = userType.Fields.Single(f =>
            string.Equals(f.Name, "Roles", StringComparison.Ordinal)
        );
        rolesField.TypeName.Should().Be("List<string>");
    }

    #endregion
}
