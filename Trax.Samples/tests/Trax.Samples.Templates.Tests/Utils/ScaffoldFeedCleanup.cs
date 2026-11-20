using Trax.Samples.Templates.Tests.Utils;

namespace Trax.Samples.Templates.Tests;

/// <summary>
/// Deletes the scaffold feed once every fixture in the run is done with it. It sits in the
/// assembly's root namespace so that it wraps every fixture in the assembly, not one folder's.
/// </summary>
[SetUpFixture]
public class ScaffoldFeedCleanup
{
    [OneTimeTearDown]
    public void DeleteTheFeed() => ScaffoldFeed.Delete();
}
