using AwesomeAssertions;
using Microsoft.Playwright;

namespace Trax.Samples.Bakery.Browser;

/// <summary>
/// The bakery's page in a browser, each person on a page of their own (SAM-61): a customer places an order, a baker
/// clocking in opens the shop, and the customer's page shows the order in the oven. No page carries a credential it
/// was not given at sign-in.
/// </summary>
[TestFixture]
public class BakeryPageTests
{
    private static readonly LocatorAssertionsToContainTextOptions Soon = new() { Timeout = 15_000 };

    [Test]
    public async Task A_baker_clocking_in_bakes_a_customers_order()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var ada = await SignInAsync($"Ada{suffix}", "customer");
        var kit = await SignInAsync($"Kit{suffix}", "baker");

        await Assertions
            .Expect(ada.Locator("[data-order-state]"))
            .ToContainTextAsync("Draft", Soon);
        await ada.GetByLabel("Loaves").FillAsync("3");
        await ada.GetByRole(AriaRole.Button, new() { Name = "Order", Exact = true }).ClickAsync();
        await ada.GetByRole(AriaRole.Button, new() { Name = "Place", Exact = true }).ClickAsync();
        await Assertions
            .Expect(ada.GetByRole(AriaRole.Region, new() { Name = "Your orders" }))
            .ToContainTextAsync("3 loaves: Waiting for the oven", Soon);

        await Assertions.Expect(kit.Locator("[data-shift-state]")).ToContainTextAsync("Off", Soon);
        await kit.GetByRole(AriaRole.Button, new() { Name = "Clock in" }).ClickAsync();
        await Assertions
            .Expect(kit.Locator("[data-shop-sign]"))
            .ToContainTextAsync("We're open", Soon);

        await Assertions
            .Expect(ada.GetByRole(AriaRole.Region, new() { Name = "Your orders" }))
            .ToContainTextAsync("3 loaves: In the oven", Soon);
    }

    [Test]
    public async Task An_empty_order_is_refused_with_the_glossarys_message()
    {
        var ada = await SignInAsync($"Ada{Guid.NewGuid():N}"[..10], "customer");

        await Assertions
            .Expect(ada.Locator("[data-order-state]"))
            .ToContainTextAsync("Draft", Soon);
        await ada.GetByLabel("Loaves").FillAsync("0");
        await ada.GetByRole(AriaRole.Button, new() { Name = "Order", Exact = true }).ClickAsync();
        await ada.GetByRole(AriaRole.Button, new() { Name = "Place", Exact = true }).ClickAsync();

        await Assertions
            .Expect(ada.GetByRole(AriaRole.Alert))
            .ToContainTextAsync("Add something first.", Soon);
    }

    [Test]
    public void The_built_page_carries_no_credential()
    {
        var bundle = string.Concat(
            Directory
                .EnumerateFiles(BrowserSetup.Dist, "*", SearchOption.AllDirectories)
                .Select(File.ReadAllText)
        );

        bundle.Should().NotContain("do-not-use-in-production");
        bundle.Should().NotContain("eyJ", "a JSON web token starts so");
    }

    private static async Task<IPage> SignInAsync(string name, string role)
    {
        var page = await BrowserSetup.NewPageAsync();
        await page.GetByLabel("Name").FillAsync(name);
        await page.GetByLabel("Role").SelectOptionAsync(role);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
        try
        {
            await Assertions.Expect(page.GetByText($"Signed in as {name}")).ToBeVisibleAsync();
        }
        catch (PlaywrightException ex)
        {
            throw new AssertionException(
                $"{name} could not sign in:\n{string.Join("\n", BrowserSetup.Seen)}",
                ex
            );
        }
        return page;
    }
}
