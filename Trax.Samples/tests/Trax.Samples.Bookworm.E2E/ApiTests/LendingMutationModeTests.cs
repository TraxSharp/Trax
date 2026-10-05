using System.Text.Json;
using Trax.Samples.Bookworm.E2E.Fixtures;
using Trax.Samples.Bookworm.E2E.Utilities;

namespace Trax.Samples.Bookworm.E2E.ApiTests;

/// <summary>
/// Borrowing and returning act as the calling member, so they run only while the request is open.
/// A queued run would have no caller, and this host has no scheduler to run it, so the schema
/// offers no <c>mode</c> argument to queue one.
/// </summary>
[TestFixture]
public class LendingMutationModeTests : ApiTestFixture
{
    [TestCase("borrowBook")]
    [TestCase("returnBook")]
    public async Task A_lending_mutation_has_no_mode_argument(string field)
    {
        var doc = await GraphQL.PostAsync(
            "{ __schema { types { name fields { name args { name } } } } }"
        );
        GraphQLClient.HasErrors(doc).Should().BeFalse(doc.RootElement.GetRawText());

        var arguments = doc
            .RootElement.GetProperty("data")
            .GetProperty("__schema")
            .GetProperty("types")
            .EnumerateArray()
            .Where(t => t.GetProperty("fields").ValueKind == JsonValueKind.Array)
            .SelectMany(t => t.GetProperty("fields").EnumerateArray())
            .Where(f => f.GetProperty("name").GetString() == field)
            .Select(f =>
                f.GetProperty("args")
                    .EnumerateArray()
                    .Select(a => a.GetProperty("name").GetString())
                    .ToList()
            )
            .ToList();

        arguments.Should().ContainSingle($"the schema serves {field} once");
        arguments[0].Should().NotContain("mode", "a queued lending run would have no caller");
    }
}
