using Trax.Dashboard.Configuration;
using Trax.Dashboard.Services.LocalStorage;
using Trax.Dashboard.Services.ThemeState;
using Microsoft.AspNetCore.Components;

namespace Trax.Dashboard.Components.Layout;

public partial class DashboardLayout
{
    [Inject]
    private IThemeStateService ThemeStateService { get; set; } = default!;

    [Inject]
    private ILocalStorageService LocalStorage { get; set; } = default!;

    [Inject]
    private DashboardOptions Options { get; set; } = default!;

    public bool SidebarExpanded { get; set; } = true;

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
