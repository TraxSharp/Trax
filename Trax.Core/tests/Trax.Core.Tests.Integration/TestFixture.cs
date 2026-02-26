namespace Trax.Core.Tests.Integration;

[SetUpFixture]
public class TestFixture
{
    [OneTimeSetUp]
    public async Task RunBeforeAnyTests() { }

    [OneTimeTearDown]
    public async Task RunAfterAnyTests() { }
}
