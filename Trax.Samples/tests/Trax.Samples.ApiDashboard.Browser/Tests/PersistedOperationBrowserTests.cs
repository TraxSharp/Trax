using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Trax.Samples.ApiDashboard.Browser.Fixtures;
using Trax.Samples.ApiDashboard.Browser.Pages;

namespace Trax.Samples.ApiDashboard.Browser.Tests;

/// <summary>
/// Persisted operations under API admin: an upload is stored, or refused with the schema's reason; an operation's
/// page deactivates it with a reason and restores it; and a caller outside the management gate is not served the
/// page at all.
/// </summary>
[TestFixture]
public class PersistedOperationBrowserTests : ConsoleTest
{
    private static readonly float Render = BrowserTimeouts.Render.Ms();

    [Test]
    [Covers("route:/api-admin/persisted-operations")]
    [Covers("mutation:UploadPersistedOperation")]
    public async Task Upload_stores_a_document_the_schema_accepts()
    {
        var name = $"browserUpload{Guid.NewGuid():N}"[..21];
        var id = $"{name}.v1";
        var list = new PersistedOperationsPage(Console);
        await list.OpenAsync(Caller.Operator);

        await Console.Button("Upload").ClickAsync(new() { Timeout = Render });
        var dialog = Console.Dialog("Upload persisted operation");
        await dialog.GetByLabel("Id", new() { Exact = true }).FillAsync(id);
        await dialog
            .GetByLabel("Document", new() { Exact = true })
            .FillAsync($"query {name} {{ operations {{ hosts {{ name }} }} }}");
        var sent = Console.SendsAsync("UploadPersistedOperation");
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await sent;

        await Assertions.Expect(dialog).ToHaveCountAsync(0, new() { Timeout = Render });
        await Assertions.Expect(list.Operation(id)).ToBeVisibleAsync(new() { Timeout = Render });
        var stored = await Stored(id);
        stored.Should().NotBeNull();
        stored!.IsActive.Should().BeTrue();
    }

    [Test]
    [Covers("route:/api-admin/persisted-operations")]
    [Covers("mutation:UploadPersistedOperation")]
    public async Task Upload_refuses_a_document_the_schema_does_not_validate_and_says_why()
    {
        var id = $"browserBroken{Guid.NewGuid():N}"[..21] + ".v1";
        var list = new PersistedOperationsPage(Console);
        await list.OpenAsync(Caller.Operator);

        await Console.Button("Upload").ClickAsync(new() { Timeout = Render });
        var dialog = Console.Dialog("Upload persisted operation");
        await dialog.GetByLabel("Id", new() { Exact = true }).FillAsync(id);
        await dialog
            .GetByLabel("Document", new() { Exact = true })
            .FillAsync("query broken { operations { noSuchField } }");
        var sent = Console.SendsAsync("UploadPersistedOperation");
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await sent;

        await Assertions
            .Expect(dialog.GetByRole(AriaRole.Alert).First)
            .ToBeVisibleAsync(new() { Timeout = Render });
        (await Stored(id)).Should().BeNull("a refused upload stores nothing");
    }

    [Test]
    [Covers("route:/api-admin/persisted-operations/:id")]
    [Covers("mutation:DeactivatePersistedOperation")]
    [Covers("mutation:RestorePersistedOperation")]
    public async Task An_operations_page_deactivates_it_with_a_reason_and_restores_it()
    {
        var operation = await Seed.PersistedOperationAsync();
        var page = new PersistedOperationPage(Console, operation.Id);
        await page.OpenAsync(Caller.Operator);
        await Assertions
            .Expect(page.DetailField("Active"))
            .ToHaveTextAsync("Yes", new() { Timeout = Render });
        await Assertions.Expect(page.Main).ToContainTextAsync(operation.Document);

        var deactivated = Console.SendsAsync("DeactivatePersistedOperation");
        await Console.Button("Deactivate").ClickAsync();
        var dialog = Console.Dialog("Deactivate persisted operation");
        await dialog.GetByLabel("Reason", new() { Exact = true }).FillAsync("Replaced by v2.");
        await dialog
            .GetByRole(AriaRole.Button, new() { Name = "Deactivate", Exact = true })
            .ClickAsync();
        await deactivated;

        await Assertions
            .Expect(page.DetailField("Active"))
            .ToHaveTextAsync("No", new() { Timeout = Render });
        await Assertions
            .Expect(page.DetailField("Deprecation reason"))
            .ToHaveTextAsync("Replaced by v2.");
        (await Stored(operation.Id))!.IsActive.Should().BeFalse();

        var restored = Console.SendsAsync("RestorePersistedOperation");
        await Console.Button("Restore").ClickAsync();
        await restored;

        await Assertions
            .Expect(page.DetailField("Active"))
            .ToHaveTextAsync("Yes", new() { Timeout = Render });
        (await Stored(operation.Id))!.IsActive.Should().BeTrue();
    }

    [Test]
    [Covers("route:/api-admin/persisted-operations/:id")]
    public async Task An_operations_page_is_not_served_to_a_caller_who_may_not_manage_them()
    {
        var operation = await Seed.PersistedOperationAsync();
        var page = new PersistedOperationPage(Console, operation.Id);
        await page.OpenAsync(Caller.NoManagement);

        await Assertions
            .Expect(Page.GetByText("your account may not manage them"))
            .ToBeVisibleAsync(new() { Timeout = Render });
        await Assertions.Expect(Console.Button("Deactivate")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Main).Not.ToContainTextAsync(operation.Document);
        (await Stored(operation.Id))!.IsActive.Should().BeTrue();
    }

    private static Task<Trax.Effect.Models.PersistedOperation.PersistedOperation?> Stored(
        string id
    ) =>
        Host.QueryAsync(dc =>
            dc.PersistedOperations.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id)
        );
}
