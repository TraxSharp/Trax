namespace Trax.Core.Tests.Unit;

[SetUpFixture]
public class TestFixture
{
    [OneTimeSetUp]
    public async Task RunBeforeAnyTests() { }

    [OneTimeTearDown]
    public async Task RunAfterAnyTests() { }
}
