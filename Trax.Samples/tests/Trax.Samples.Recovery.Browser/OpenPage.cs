using Microsoft.Playwright;
using Trax.Samples.Recovery.Browser.Factories;
using Trax.Samples.Recovery.Browser.Fixtures;
using Trax.Samples.Recovery.E2E.Factories;
using Trax.Samples.Recovery.E2E.Utilities;

// In the assembly root namespace so this [SetUpFixture] covers every test namespace.
namespace Trax.Samples.Recovery.Browser;

/// <summary>A <see cref="RecoveryPage"/> whose context closes when it is disposed.</summary>
public sealed class OpenPage(RecoveryPage page) : IAsyncDisposable
{
    public RecoveryPage Recovery { get; } = page;

    public Task OpenTheWizardAsync() => Recovery.OpenTheWizardAsync();

    public ValueTask DisposeAsync() => new(Recovery.Context.CloseAsync());
}
