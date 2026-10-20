namespace Shopfront.Tests.IntegrationTests;

// The host tests write their documents as text, which the hub refuses. For the whole run they set
// the Development-only switch that lets text run; a test that checks the refusal sets it back.
[SetUpFixture]
public class DocumentsAsText
{
    private const string Switch = "PersistedOperations__Require";

    [OneTimeSetUp]
    public void LetTextRun() => Environment.SetEnvironmentVariable(Switch, "false");

    [OneTimeTearDown]
    public void Restore() => Environment.SetEnvironmentVariable(Switch, null);
}
