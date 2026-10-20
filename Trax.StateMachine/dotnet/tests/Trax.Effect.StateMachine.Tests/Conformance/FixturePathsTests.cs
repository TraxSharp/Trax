using AwesomeAssertions;
using Trax.Effect.StateMachine.Tests.Helpers;

namespace Trax.Effect.StateMachine.Tests.Conformance;

/// <summary>
/// The conformance tests read <c>Trax.Api.StateMachine/machines</c>, which is part of the repository. A build that
/// cannot find it must fail them rather than skip them, or every cross-engine check reports green unchecked.
/// </summary>
public class FixturePathsTests
{
    [Test]
    public void A_directory_with_no_machines_folder_above_it_throws_naming_where_it_looked()
    {
        var start = Path.Combine(Path.GetTempPath(), $"trax-no-fixtures-{Guid.NewGuid():N}", "bin");
        Directory.CreateDirectory(start);
        try
        {
            var find = () => FixturePaths.Find(start);

            find.Should()
                .Throw<DirectoryNotFoundException>()
                .WithMessage($"*{Path.Combine(start, "Trax.Api.StateMachine", "machines")}*");
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(start)!, recursive: true);
        }
    }

    [Test]
    public void The_test_output_finds_the_shared_machines_folder()
    {
        Directory
            .Exists(Path.Combine(FixturePaths.MachinesRoot, "turnstile", "fixtures"))
            .Should()
            .BeTrue();
    }
}
