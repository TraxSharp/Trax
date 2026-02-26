namespace Trax.Core.Tests;

public interface ITestFixture
{
    Task RunBeforeAnyTests();

    Task RunAfterAnyTests();
}
