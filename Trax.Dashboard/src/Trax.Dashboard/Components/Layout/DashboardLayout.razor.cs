using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Trax.Dashboard.Configuration;
using Trax.Dashboard.Services.LocalStorage;
using Trax.Dashboard.Services.ThemeState;

namespace Trax.Dashboard.Components.Layout;

/// <summary>
/// The layout every dashboard page renders in: the Radzen theme, header, collapsible sidebar and
/// footer. The theme and the sidebar state are restored from the browser's <c>localStorage</c>
/// after the first render. Part of the dashboard UI; not intended to be used directly.
/// </summary>
public partial class DashboardLayout
{
    [Inject]
    private IThemeStateService ThemeStateService { get; set; } = default!;

    [Inject]
    private ILocalStorageService LocalStorage { get; set; } = default!;

    [Inject]
    private DashboardOptions Options { get; set; } = default!;

    /// <summary>
    /// Whether the sidebar is expanded. Not a component parameter: the layout sets it from storage
    /// and from the header's toggle, and persists each toggle.
    /// </summary>
    private bool SidebarExpanded { get; set; } = true;

    private ErrorBoundary? _pageErrorBoundary;

    /// <summary>
    /// Clears a page error when the page changes (the router sets a new <c>Body</c> on every
    /// navigation), so the next page renders instead of the error that stopped the previous one.
    /// </summary>
    protected override void OnParametersSet() => _pageErrorBoundary?.Recover();

    private void RecoverPage() => _pageErrorBoundary?.Recover();

    /// <summary>
    /// On the first render only, initializes the theme and restores the stored sidebar state,
    /// then re-renders. Both need JS interop, which is unavailable before the first render.
    /// </summary>
    /// <param name="firstRender"><see langword="true"/> on the component's first render.</param>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await ThemeStateService.InitializeAsync();

            var stored = await LocalStorage.GetAsync<bool?>(StorageKeys.SidebarExpanded);
            if (stored.HasValue)
                SidebarExpanded = stored.Value;

            StateHasChanged();
        }
    }

    private async Task ToggleTheme()
    {
        await ThemeStateService.ToggleThemeAsync();
        StateHasChanged();
    }

    private async Task ToggleSidebar()
    {
        SidebarExpanded = !SidebarExpanded;
        await LocalStorage.SetAsync(StorageKeys.SidebarExpanded, SidebarExpanded);
    }
}
