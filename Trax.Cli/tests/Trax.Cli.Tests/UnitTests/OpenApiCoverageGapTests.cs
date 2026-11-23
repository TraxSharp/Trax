using AwesomeAssertions;
using Trax.Cli.Schema.OpenApi;
using Trax.Core.Extensions;

namespace Trax.Cli.Tests.UnitTests;

public class OpenApiCoverageGapTests
{
    private static string FixturePath(string name) =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Schemas", name);

    [Test]
    public async Task Parse_MalformedDocument_Throws()
    {
        var path = Path.GetTempFileName();
        try
        {
            await
            // Empty/invalid OpenAPI document — reader returns null and the parser
            // converts diagnostic errors into InvalidOperationException.
            File.WriteAllTextAsync(path, "{}");
            var parser = new OpenApiSchemaParser();

            Func<Task> act = () => parser.ParseAsync(path);

            await act.Should().ThrowAsync<Exception>().ConfigureAwait(false);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task Parse_InlineResponseObject_BuildsOutputType()
    {
        var parser = new OpenApiSchemaParser();
        var schema = await parser
            .ParseAsync(FixturePath("inline-response-object.json"))
            .ConfigureAwait(false);

        var listOp = schema.Operations.Single(o =>
            string.Equals(o.Name, "ListWidgets", StringComparison.Ordinal)
        );
        listOp.OutputType.Should().NotBeNull();
        listOp.OutputType.NotNull().Name.Should().Be("ListWidgetsOutput");
        listOp.OutputType.Fields.Should().Contain(f => f.Name == "Count");
        listOp.OutputType.Fields.Should().Contain(f => f.Name == "Label");

        // No content => Unit output
        var noContentOp = schema.Operations.Single(o =>
            string.Equals(o.Name, "NoContent", StringComparison.Ordinal)
        );
        noContentOp.OutputType.NotNull().Name.Should().Be("Unit");
        noContentOp.OutputType.IsBuiltIn.Should().BeTrue();

        // Non-JSON (binary) => Unit output
        var binaryOp = schema.Operations.Single(o =>
            string.Equals(o.Name, "BinaryDownload", StringComparison.Ordinal)
        );
        binaryOp.OutputType.NotNull().Name.Should().Be("Unit");
        binaryOp.OutputType.IsBuiltIn.Should().BeTrue();
    }

    [Test]
    public async Task Parse_RefRequestBody_PullsFieldsIntoInput()
    {
        var parser = new OpenApiSchemaParser();
        var schema = await parser
            .ParseAsync(FixturePath("refbody-input.json"))
            .ConfigureAwait(false);

        var op = schema.Operations.Single(o =>
            string.Equals(o.Name, "CreateThing", StringComparison.Ordinal)
        );
        op.InputType.Should().NotBeNull();
        op.InputType.NotNull().Fields.Should().Contain(f => f.Name == "Name");
        op.InputType.Fields.Should().Contain(f => f.Name == "Color");
    }
}
