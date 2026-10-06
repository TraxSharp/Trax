using Trax.Samples.Templates.Tests.Utils;

/// <summary>
/// Deletes the scaffold feed once every fixture in the run is done with it. It has no namespace
/// so that it wraps every fixture in the assembly, not one namespace.
/// </summary>
[SetUpFixture]
public class ScaffoldFeedCleanup
{
    [OneTimeTearDown]
    public void DeleteTheFeed() => ScaffoldFeed.Delete();
}
