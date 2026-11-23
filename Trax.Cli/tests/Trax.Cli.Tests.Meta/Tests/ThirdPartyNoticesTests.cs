namespace Trax.Cli.Tests.Meta.Tests;

/// <summary>
/// The trax tool ships its dependencies' assemblies, so its package carries their notices: every package with a
/// runtime assembly, its licence and copyright, each licence's text, and an Apache-2.0 package's NOTICE file. The
/// committed file is regenerated from the tool's runtime closure, so a dependency added, removed or relicensed lands
/// in the diff.
///
/// <para>Not ADR-enforcing: it keeps a licence obligation the tool's dependencies impose (Apache-2.0 and MIT ask a
/// redistributor to carry their licence and notices), which is a fact about them rather than a choice between
/// designs.</para>
/// </summary>
[TestFixture]
public class ThirdPartyNoticesTests
{
    [Test]
    public async Task The_notices_name_every_package_the_tool_ships()
    {
        var expected = await ThirdPartyNotices.GenerateAsync().ConfigureAwait(false);
        var committed = File.Exists(ThirdPartyNotices.CommittedPath)
            ? await File.ReadAllTextAsync(ThirdPartyNotices.CommittedPath).ConfigureAwait(false)
            : "";

        if (
            !string.Equals(
                committed.Replace("\r\n", "\n", StringComparison.Ordinal),
                expected,
                StringComparison.Ordinal
            )
        )
        {
            await File.WriteAllTextAsync(ThirdPartyNotices.CommittedPath, expected)
                .ConfigureAwait(false);
            await TestContext.Out.WriteLineAsync(expected).ConfigureAwait(false);
            Assert.Fail(
                $"{RepoRoot.Relative(ThirdPartyNotices.CommittedPath)} does not match the tool's runtime dependencies. "
                    + "It has been rewritten (and printed above): review the change and commit it."
            );
        }
    }

    [Test]
    public async Task Every_shipped_package_names_its_licence()
    {
        var packages = await ThirdPartyNotices.ShippedAsync().ConfigureAwait(false);

        packages.Should().NotBeEmpty();
        packages
            .Where(p => p.Expression is null && p.LicenseFile is null)
            .Select(p => $"{p.Id} ({p.LicenseUrl ?? "no licence at all"})")
            .Should()
            .BeEmpty(
                "a package known only by a licence URL needs its licence read and recorded by hand"
            );
    }

    [TestCase("MIT", "MIT")]
    [TestCase("MIT OR Apache-2.0", "MIT,Apache-2.0")]
    [TestCase(
        "(Apache-2.0 WITH LLVM-exception) AND BSD-3-Clause",
        "Apache-2.0,LLVM-exception,BSD-3-Clause"
    )]
    public void A_licence_expression_names_each_licence_in_it(string expression, string ids) =>
        ThirdPartyNotices.LicenceIds(expression).Should().Equal(ids.Split(','));

    [Test]
    public async Task The_package_packs_the_committed_notices()
    {
        var project = XDocument.Parse(
            await File.ReadAllTextAsync(RepoRoot.Combine("src", "Trax.Cli", "Trax.Cli.csproj"))
                .ConfigureAwait(false)
        );

        project
            .Descendants("None")
            .Should()
            .Contain(n =>
                string.Equals(
                    (string?)n.Attribute("Include"),
                    "THIRD-PARTY-NOTICES.txt",
                    StringComparison.Ordinal
                ) && string.Equals((string?)n.Attribute("Pack"), "true", StringComparison.Ordinal)
            );
    }
}
