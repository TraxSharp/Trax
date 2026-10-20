using Shopfront.Machines.Checkout;
using Trax.Effect.StateMachine.Testing;

namespace Shopfront.Tests.UnitTests;

[TestFixture]
public class CheckoutMachineTests
{
    // The solution's folder, where machines/ is.
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !dir.EnumerateFiles("*.slnx").Any())
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("No solution above the tests.");
    }

    [Test]
    public void Checkout_MatchesItsExportedDefinitionAndCorpus()
    {
        var folder = Path.Combine(Root(), "machines", "checkout");

        MachineConformance
            .Check(
                new CheckoutMachine(),
                Path.Combine(folder, "checkout.ir.json"),
                Path.Combine(folder, "differential.json")
            )
            .Should()
            .BeEmpty("the machine changed without `dotnet trax machine generate`");
    }
}
