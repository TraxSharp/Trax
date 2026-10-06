using System.Text.Json;
using Trax.Samples.ChatService.E2E.Fixtures;

namespace Trax.Samples.ChatService.E2E.ChatApiTests;

/// <summary>
/// The chat mutations act as the caller, so they run only while the request is open. A queued run
/// would have no caller, and this host has no scheduler to run it, so the schema offers no
/// <c>mode</c> argument to queue one.
/// </summary>
[TestFixture]
public class MutationModeTests : ChatApiTestFixture
{
    [TestCase("createChatRoom")]
    [TestCase("joinChatRoom")]
    [TestCase("inviteToChatRoom")]
    [TestCase("sendMessage")]
    public async Task A_chat_mutation_has_no_mode_argument(string field)
    {
        var response = await GraphQL.SendAsync(
            "{ __schema { types { name fields { name args { name } } } } }"
        );
        response.HasErrors.Should().BeFalse(response.FirstErrorMessage);

        var arguments = response
            .GetData("__schema", "types")
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
        arguments[0].Should().NotContain("mode", "a queued chat run would have no caller");
    }
}
