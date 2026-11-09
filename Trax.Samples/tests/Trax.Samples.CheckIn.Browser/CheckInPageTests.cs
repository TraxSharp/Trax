using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.Playwright;

namespace Trax.Samples.CheckIn.Browser;

/// <summary>
/// The check-in page in a browser, each person on a page of their own: a gate agent opens their desk and every booked
/// passenger can check in; one is refused without a passport, gives one and gets a boarding pass, one is turned down
/// for an expired passport, and one misses the flight when the agent closes the gate. Each page lights the step of the
/// feature that says where its booking is. No page carries a credential it was not given at sign-in.
/// </summary>
[TestFixture]
public class CheckInPageTests
{
    private static readonly LocatorAssertionsToContainTextOptions Soon = new() { Timeout = 15_000 };

    // One story, since every booking and the agent share the one flight: the broadcast that opens check-in only reaches
    // bookings made before it, and the gate closing only those still open.
    [Test]
    public async Task A_gate_agent_opens_check_in_and_closes_the_gate_on_the_passengers()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var kit = await SignInAsync($"Kit{suffix}", "agent");
        await Assertions
            .Expect(kit.Locator("[data-departures]"))
            .ToContainTextAsync("Scheduled", Soon);

        var ada = await SignInAsync($"Ada{suffix}", "passenger");
        var bo = await SignInAsync($"Bo{suffix}", "passenger");
        var cy = await SignInAsync($"Cy{suffix}", "passenger");
        foreach (var page in new[] { ada, bo, cy })
            await Assertions
                .Expect(page.Locator("[data-booking-state]"))
                .ToContainTextAsync("Booked", Soon);

        await Assertions
            .Expect(kit.Locator("[data-desk-state]"))
            .ToContainTextAsync("Waiting", Soon);
        await kit.GetByRole(AriaRole.Button, new() { Name = "Open the desk" }).ClickAsync();
        await Assertions
            .Expect(kit.Locator("[data-departures]"))
            .ToContainTextAsync("Check-in open", Soon);
        foreach (var page in new[] { ada, bo, cy })
            await Assertions
                .Expect(page.Locator("[data-booking-state]"))
                .ToContainTextAsync("Open", Soon);

        // No passport yet: the server refuses, with the message the feature gives.
        await ada.GetByRole(AriaRole.Button, new() { Name = "Check in", Exact = true })
            .ClickAsync();
        await Assertions
            .Expect(ada.GetByRole(AriaRole.Alert))
            .ToContainTextAsync("Add your passport number first.", Soon);
        await ada.GetByLabel("Passport number").FillAsync("X1234567");
        await ada.GetByRole(AriaRole.Button, new() { Name = "Give passport" }).ClickAsync();
        await Assertions.Expect(ada.Locator(".context")).ToContainTextAsync("X1234567", Soon);
        await ada.GetByLabel("Seat").FillAsync("14C");
        await ada.GetByRole(AriaRole.Button, new() { Name = "Check in", Exact = true })
            .ClickAsync();
        await Assertions
            .Expect(ada.GetByRole(AriaRole.Region, new() { Name = "Your bookings" }))
            .ToContainTextAsync(new Regex("seat 14C.*Checked in"), Soon);
        await ExpectSourceAsync(ada, "checking-in.feature", "Ada's booking is checked in");

        // A passport number ending in 0 has expired: the train turns it down.
        await bo.GetByLabel("Passport number").FillAsync("X0000000");
        await bo.GetByRole(AriaRole.Button, new() { Name = "Give passport" }).ClickAsync();
        await Assertions.Expect(bo.Locator(".context")).ToContainTextAsync("X0000000", Soon);
        await bo.GetByRole(AriaRole.Button, new() { Name = "Check in", Exact = true }).ClickAsync();
        await Assertions
            .Expect(bo.GetByRole(AriaRole.Region, new() { Name = "Your bookings" }))
            .ToContainTextAsync("Passport not accepted", Soon);
        await ExpectSourceAsync(bo, "checking-in.feature", "Ada's booking was turned down");

        await kit.GetByRole(AriaRole.Button, new() { Name = "Close the gate" }).ClickAsync();
        await Assertions
            .Expect(kit.Locator("[data-departures]"))
            .ToContainTextAsync("Gate closed", Soon);
        await Assertions
            .Expect(cy.GetByRole(AriaRole.Region, new() { Name = "Your bookings" }))
            .ToContainTextAsync("Missed the flight", Soon);
        await ExpectSourceAsync(cy, "checking-in.feature", "Ada's booking missed the flight");
        // Ada was checked in, so the gate closing left her booking where it was.
        await Assertions
            .Expect(ada.GetByRole(AriaRole.Region, new() { Name = "Your bookings" }))
            .ToContainTextAsync("Checked in", Soon);
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

    // The feature-source panel shows the file whose scenario ends where the booking is now, and marks that step.
    private static async Task ExpectSourceAsync(IPage page, string file, string step)
    {
        var source = page.GetByRole(AriaRole.Region, new() { Name = "Feature source" });
        await Assertions
            .Expect(source.GetByRole(AriaRole.Tab, new() { Selected = true }))
            .ToHaveTextAsync(file, new() { Timeout = 15_000 });
        await Assertions.Expect(source.Locator(".hit").First).ToContainTextAsync(step, Soon);
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
