using AwesomeAssertions;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Tests.Integration.UnitTests.Services;

/// <summary>
/// <see cref="BranchPaths.Enclosing"/> lists exactly the paths <see cref="BranchPaths.Encloses"/>
/// accepts, so a lookup by the list and a lookup by the predicate route on the same decision.
/// </summary>
public class BranchPathsTests
{
    [TestCase("")]
    [TestCase("Parallel#0/a")]
    [TestCase("Parallel#0/a/Switch#1/Express")]
    [TestCase("Parallel#0/a/Parallel#0/x")]
    public void Enclosing_lists_every_path_that_encloses_the_path_and_no_other(string path)
    {
        var enclosing = BranchPaths.Enclosing(path);

        enclosing.Should().OnlyContain(outer => BranchPaths.Encloses(outer, path));
        enclosing.Should().OnlyHaveUniqueItems();

        // Every candidate a path could be compared with: each prefix of it, and its siblings'.
        var candidates = Enumerable
            .Range(0, path.Length + 1)
            .Select(n => path[..n])
            .Concat([path + "/x", "Parallel#0/b", "Parallel#0/ab"]);
        candidates
            .Where(outer => BranchPaths.Encloses(outer, path))
            .Should()
            .BeEquivalentTo(enclosing);
    }
}
