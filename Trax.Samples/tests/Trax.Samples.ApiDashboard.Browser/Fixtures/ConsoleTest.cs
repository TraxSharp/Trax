using Microsoft.Playwright;
using Trax.Samples.ApiDashboard.Browser.Factories;
using Trax.Samples.ApiDashboard.Browser.Pages;

namespace Trax.Samples.ApiDashboard.Browser.Fixtures;

/// <summary>
/// The base of every console browser test: a fresh browser context and page on the shared host, a
/// <see cref="Fixtures.Seed"/> for the states the test needs, and, once the test is over, the checks every test
/// owes (<see cref="PageChecks"/>): no console error, no CSP violation, no request off the console's origin, and no
/// document text where the host requires persisted operations. A failed check fails the test.
/// </summary>
/// <remarks>
/// Tests run one at a time: they share one host, and a seed such as a paused dispatcher changes it for whoever runs
/// alongside.
/// </remarks>
[NonParallelizable]
public abstract class ConsoleTest
{
    private IBrowserContext _context = null!;

    protected static ConsoleHost Host => ConsoleSetup.Host;

    protected IPage Page { get; private set; } = null!;

    protected ConsolePage Console { get; private set; } = null!;

    protected PageChecks Checks { get; private set; } = null!;

    protected Seed Seed { get; private set; } = null!;

    [SetUp]
    public async Task OpenContextAsync()
    {
        Seed = new Seed(Host);
        _context = await ConsoleSetup.Browser.NewContextAsync();
        Page = await _context.NewPageAsync();
        Checks = await PageChecks.WatchAsync(
            Page,
            Host.Address,
            ConsoleSetup.DocumentIds,
            Host.RequiresPersistedOperations
        );
        Console = new ConsolePage(Page, Host.Address);
    }

    [TearDown]
    public async Task CloseContextAsync()
    {
        IReadOnlyList<string> problems = [];
        try
        {
            problems = await Checks.ProblemsAsync(Page);
        }
        finally
        {
            await _context.CloseAsync();
            await Seed.DisposeAsync();
        }
        problems
            .Should()
            .BeEmpty(
                "a console page logs no error, breaks no Content-Security-Policy, reaches no other origin and, "
                    + "where persisted operations are required, sends no document text"
            );
    }
}
