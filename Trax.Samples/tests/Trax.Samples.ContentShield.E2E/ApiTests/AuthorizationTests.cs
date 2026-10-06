using System.Net;
using System.Text;
using Trax.Samples.ContentShield.E2E.Fixtures;

namespace Trax.Samples.ContentShield.E2E.ApiTests;

/// <summary>
/// Who may do what: anyone may submit content and look up a result, only a moderator may pull a
/// report or notify a user, and only the API (holding the signing key) may post work to the runner.
/// </summary>
[TestFixture]
public class AuthorizationTests : ApiTestFixture
{
    [Test]
    public async Task Anonymous_caller_cannot_generate_a_report()
    {
        var result = await GetGraphQLClient()
            .SendAsync(
                """mutation { dispatch { reports { generateModerationReport(input: { reportPeriod: "Daily" }) { externalId } } } }"""
            );

        result.FirstErrorMessage.Should().Be("Not authorized.");
    }

    [Test]
    public async Task Anonymous_caller_cannot_send_a_violation_notice()
    {
        var result = await GetGraphQLClient()
            .SendAsync(
                """mutation { dispatch { sendViolationNotice(input: { contentId: "c", violationType: "spam", userId: "u" }) { externalId } } }"""
            );

        result.FirstErrorMessage.Should().Be("Not authorized.");
    }

    [TestCase("/trax/execute")]
    [TestCase("/trax/run")]
    public async Task Runner_refuses_an_unsigned_request(string path)
    {
        using var http = new HttpClient();
        var response = await http.PostAsync(
            $"{Runner.BaseUrl}{path}",
            new StringContent("{}", Encoding.UTF8, "application/json")
        );

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
