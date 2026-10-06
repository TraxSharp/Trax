namespace Trax.Core.Tests;

public interface ITestSetup
{
    Task TestSetUp();

    Task TestTearDown();
}
