using AwesomeAssertions;

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
        var bundle = Directory
            .EnumerateFiles(BrowserSetup.Dist, "*.js", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToList();

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
}
