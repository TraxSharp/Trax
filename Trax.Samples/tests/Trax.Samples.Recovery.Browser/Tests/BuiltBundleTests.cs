using System.Text.Json.Nodes;
using Trax.Samples.Recovery.Auth;
using Trax.Samples.Recovery.Browser.Fixtures;

namespace Trax.Samples.Recovery.Browser.Tests;

/// <summary>
/// The page the build ships signs in as the demo user and sends nothing under <c>operations</c>: the built bundle
/// holds no operator key, and no persisted id the page could send is the operator's.
/// </summary>
[TestFixture]
public class BuiltBundleTests
{
    [Test]
    public void Built_bundle_holds_no_operator_key()
    {
        var bundle = Bundle();

        bundle.Should().NotBeEmpty("the page was built");
        bundle
            .Should()
            .NotContain(
                text => text.Contains("recovery-operator-key", StringComparison.Ordinal),
                "the page is a user's; the operator's key belongs to the dashboard"
            );
        bundle
            .Should()
            .NotContain(
                text => text.Contains("recovery_", StringComparison.Ordinal),
                "the page sends only its generated documents, never the retired hand-written ids"
            );
    }

    [Test]
    public void Built_bundle_holds_no_operator_only_document()
    {
        var manifest = JsonNode.Parse(
            File.ReadAllText(
                Path.Combine(RecoveryClientBuild.ClientFolder, "src", "trax", "manifest.json")
            )
        )!;
        var forbidden = manifest["trains"]!
            .AsArray()
            .Where(t => !UserMayRun(t!["requires"]!))
            .SelectMany(t => t!["documents"]!.AsArray())
            .Concat(Documents(manifest).Where(SelectsOperations))
            .Select(d => d!["id"]!.GetValue<string>())
            .Distinct()
            .ToList();
        var bundle = Bundle();

        bundle.Should().NotBeEmpty("the page was built");
        forbidden
            .Should()
            .NotBeEmpty(
                "the manifest holds the operator's trains, so the check has ids to look for"
            );
        foreach (var id in forbidden)
            bundle
                .Should()
                .NotContain(
                    text => text.Contains(id, StringComparison.Ordinal),
                    $"the page is a user's, and '{id}' is a document only an operator may send"
                );
    }

    private static List<string> Bundle() =>
        Directory
            .EnumerateFiles(BrowserSetup.Dist, "*.js", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToList();

    // A train the page's user may run: every role set it requires names the user's role.
    private static bool UserMayRun(JsonNode requires) =>
        requires["anonymous"]!.GetValue<bool>()
        || requires["roleSets"]!
            .AsArray()
            .All(set => set!.AsArray().Any(r => r!.GetValue<string>() == RecoveryRoles.User));

    private static IEnumerable<JsonNode?> Documents(JsonNode manifest) =>
        manifest["trains"]!
            .AsArray()
            .SelectMany(t => t!["documents"]!.AsArray())
            .Concat(manifest["machines"]!.AsArray().SelectMany(m => m!["documents"]!.AsArray()))
            .Concat(manifest["progress"]!.AsArray());

    private static bool SelectsOperations(JsonNode? document) =>
        document!["document"]!.GetValue<string>().Contains("operations", StringComparison.Ordinal);
}
