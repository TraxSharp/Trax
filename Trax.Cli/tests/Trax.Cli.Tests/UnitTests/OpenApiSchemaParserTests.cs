using FluentAssertions;
using Trax.Cli.Models;
using Trax.Cli.Schema.OpenApi;

namespace Trax.Cli.Tests.UnitTests;

public class OpenApiSchemaParserTests
{
    private static string FixturePath(string name) =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Schemas", name);

    #region Petstore

    [Test]
    public void Parse_Petstore_Returns4Operations()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("petstore.json"));

        schema.Operations.Should().HaveCount(4);
        schema
            .Operations.Select(o => o.Name)
            .Should()
            .BeEquivalentTo("ListPets", "CreatePet", "GetPet", "DeletePet");
    }

    [Test]
    public void Parse_Petstore_GetOperationsAreQuery()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("petstore.json"));

        schema
            .Operations.Where(o => o.Name is "ListPets" or "GetPet")
            .Should()
            .AllSatisfy(o => o.Kind.Should().Be(OperationKind.Query));
    }

    [Test]
    public void Parse_Petstore_PostAndDeleteAreMutation()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("petstore.json"));

        schema
            .Operations.Where(o => o.Name is "CreatePet" or "DeletePet")
            .Should()
            .AllSatisfy(o => o.Kind.Should().Be(OperationKind.Mutation));
    }

    [Test]
    public void Parse_Petstore_GetPetInputHasPetIdOfTypeGuid()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("petstore.json"));

        var getPet = schema.Operations.Single(o => o.Name == "GetPet");
        var petIdField = getPet.InputType.Fields.Single(f => f.Name == "PetId");
        petIdField.TypeName.Should().Be("Guid");
    }

    [Test]
    public void Parse_Petstore_DeletePetOutputIsUnit()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("petstore.json"));

        var deletePet = schema.Operations.Single(o => o.Name == "DeletePet");
        deletePet.OutputType.Name.Should().Be("Unit");
    }

    [Test]
    public void Parse_Petstore_CreatePetInputHasFieldsFromRequest()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("petstore.json"));

        var createPet = schema.Operations.Single(o => o.Name == "CreatePet");
        var nameField = createPet.InputType.Fields.Single(f => f.Name == "Name");
        var tagField = createPet.InputType.Fields.Single(f => f.Name == "Tag");

        nameField.IsRequired.Should().BeTrue();
        tagField.IsRequired.Should().BeFalse();
    }

    [Test]
    public void Parse_Petstore_AllOperationsGroupedAsPets()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("petstore.json"));

        schema.Operations.Should().AllSatisfy(o => o.Group.Should().Be("Pets"));
    }

    [Test]
    public void Parse_Petstore_OperationsHaveHttpMethodAndPath()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("petstore.json"));

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
    public void Parse_ComplexOpenApi_Returns4Operations()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("complex-openapi.json"));

        schema.Operations.Should().HaveCount(4);
    }

    [Test]
    public void Parse_ComplexOpenApi_ListUsersIsQuery_CreateUserIsMutation()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("complex-openapi.json"));

        schema.Operations.Single(o => o.Name == "ListUsers").Kind.Should().Be(OperationKind.Query);
        schema
            .Operations.Single(o => o.Name == "CreateUser")
            .Kind.Should()
            .Be(OperationKind.Mutation);
    }

    [Test]
    public void Parse_ComplexOpenApi_GetUserInputHasUserIdOfTypeLong()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("complex-openapi.json"));

        var getUser = schema.Operations.Single(o => o.Name == "GetUser");
        var userIdField = getUser.InputType.Fields.Single(f => f.Name == "UserId");
        userIdField.TypeName.Should().Be("long");
    }

    [Test]
    public void Parse_ComplexOpenApi_CreateUserInputHasRequiredAndDateTimeFields()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("complex-openapi.json"));

        var createUser = schema.Operations.Single(o => o.Name == "CreateUser");
        var emailField = createUser.InputType.Fields.Single(f => f.Name == "Email");
        var nameField = createUser.InputType.Fields.Single(f => f.Name == "Name");
        var birthDateField = createUser.InputType.Fields.Single(f => f.Name == "BirthDate");

        emailField.IsRequired.Should().BeTrue();
        nameField.IsRequired.Should().BeTrue();
        birthDateField.TypeName.Should().Be("DateTime");
    }

    [Test]
    public void Parse_ComplexOpenApi_HealthCheckGetsASynthesizedName()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("complex-openapi.json"));

        var healthOp = schema.Operations.SingleOrDefault(o => o.HttpPath == "/health");
        healthOp.Should().NotBeNull();
        healthOp!.Name.Should().NotBeNullOrWhiteSpace();
        // No operationId in the schema, so the name is synthesized from method + path
        healthOp.Name.Should().Be("GetHealth");
    }

    [Test]
    public void Parse_ComplexOpenApi_UserSchemaHasRolesAsListOfString()
    {
        var parser = new OpenApiSchemaParser();

        var schema = parser.Parse(FixturePath("complex-openapi.json"));

        var userType = schema.Types.Single(t => t.Name == "User");
        var rolesField = userType.Fields.Single(f => f.Name == "Roles");
        rolesField.TypeName.Should().Be("List<string>");
    }

    #endregion
}
