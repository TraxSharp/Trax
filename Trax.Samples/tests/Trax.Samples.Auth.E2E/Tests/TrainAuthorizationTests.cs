using Trax.Samples.Auth.E2E.Fixtures;
using Trax.Samples.Auth.E2E.Utilities;

namespace Trax.Samples.Auth.E2E.Tests;

/// <summary>
/// <c>publishArticle</c> carries <c>[TraxAuthorize(VerifiedEmail)]</c> and
/// <c>[TraxAuthorize(Roles = Editor)]</c>, which AND. A refused caller gets only the generic
/// <c>TRAX_AUTHORIZATION</c> error, and the train never runs: no article is written.
/// </summary>
[TestFixture]
public class TrainAuthorizationTests : AuthTestFixture
{
    private static string Publish(string title) =>
        $$"""
            mutation {
              dispatch {
                news {
                  publishArticle(input: { title: "{{title}}", body: "Body text", editorNote: "internal" }) {
                    output { articleId authorId }
                  }
                }
              }
            }
            """;

    private IEnumerable<Caller> RefusedCallers() =>
        [Anonymous, BobKey, BobToken, ErinKey, ErinToken, OscarKey, OscarToken];

    [Test]
    public async Task Publish_IsRefused_ForAnyoneButAVerifiedEditor()
    {
        foreach (var caller in RefusedCallers())
        {
            var title = $"refused {Guid.NewGuid():N}";

            var result = await GraphQL.SendAsync(Publish(title), caller);

            result.IsRefused.Should().BeTrue($"{caller} is not a verified editor: {result.Raw}");
            (await CountArticlesTitled(title))
                .Should()
                .Be(0, $"a refused call from {caller} must not run the train");
        }
    }

    [Test]
    public async Task Publish_ErinHoldsTheRoleButFailsThePolicy()
    {
        var who = await GraphQL.SendAsync("{ discover { whoAmI { roles } } }", ErinKey);
        who.GetData("discover", "whoAmI", "roles")[0].GetString().Should().Be("Editor");

        var result = await GraphQL.SendAsync(Publish($"erin {Guid.NewGuid():N}"), ErinKey);

        result
            .IsRefused.Should()
            .BeTrue("a policy and a role on separate attributes must both pass");
    }

    [Test]
    public async Task Publish_VerifiedEditor_Succeeds_OverEitherScheme()
    {
        foreach (
            var (caller, expectedAuthor) in new[]
            {
                (AliceKey, "TraxApiKey:alice"),
                (AliceToken, "TraxJwt:alice"),
            }
        )
        {
            var title = $"published {Guid.NewGuid():N}";

            var result = await GraphQL.SendAsync(Publish(title), caller);

            result.HasErrors.Should().BeFalse($"{caller}: {result.Raw}");
            result
                .GetData("dispatch", "news", "publishArticle", "output", "authorId")
                .GetString()
                .Should()
                .Be(expectedAuthor, "the junction stores the scheme-qualified id it was given");
            (await CountArticlesTitled(title)).Should().Be(1);
        }
    }
}
