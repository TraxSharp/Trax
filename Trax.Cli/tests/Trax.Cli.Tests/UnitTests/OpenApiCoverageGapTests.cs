using FluentAssertions;
using Trax.Cli.Schema.OpenApi;

namespace Trax.Cli.Tests.UnitTests;

public class OpenApiCoverageGapTests
{
    private static string FixturePath(string name) =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Schemas", name);

    [Test]
    public void Parse_MalformedDocument_Throws()
    {
        var path = Path.GetTempFileName();
        try
        {
            // Empty/invalid OpenAPI document — reader returns null and the parser
            // converts diagnostic errors into InvalidOperationException.
            File.WriteAllText(path, "{}");
            var parser = new OpenApiSchemaParser();

            Action act = () => parser.Parse(path);

            act.Should().Throw<Exception>();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Parse_InlineResponseObject_BuildsOutputType()
    {
        var parser = new OpenApiSchemaParser();
        var schema = parser.Parse(FixturePath("inline-response-object.json"));

        var listOp = schema.Operations.Single(o => o.Name == "ListWidgets");
        listOp.OutputType.Should().NotBeNull();
        listOp.OutputType!.Name.Should().Be("ListWidgetsOutput");
        listOp.OutputType.Fields.Should().Contain(f => f.Name == "Count");
        listOp.OutputType.Fields.Should().Contain(f => f.Name == "Label");

        // No content => Unit output
        var noContentOp = schema.Operations.Single(o => o.Name == "NoContent");
        noContentOp.OutputType!.Name.Should().Be("Unit");
        noContentOp.OutputType.IsBuiltIn.Should().BeTrue();

        // Non-JSON (binary) => Unit output
        var binaryOp = schema.Operations.Single(o => o.Name == "BinaryDownload");
        binaryOp.OutputType!.Name.Should().Be("Unit");
        binaryOp.OutputType.IsBuiltIn.Should().BeTrue();
    }

    [Test]
    public void Parse_RefRequestBody_PullsFieldsIntoInput()
    {
        var parser = new OpenApiSchemaParser();
        var schema = parser.Parse(FixturePath("refbody-input.json"));

        var op = schema.Operations.Single(o => o.Name == "CreateThing");
        op.InputType.Should().NotBeNull();
        op.InputType!.Fields.Should().Contain(f => f.Name == "Name");
        op.InputType.Fields.Should().Contain(f => f.Name == "Color");
    }
}
