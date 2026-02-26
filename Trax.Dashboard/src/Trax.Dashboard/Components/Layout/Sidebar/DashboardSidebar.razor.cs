using Microsoft.AspNetCore.Components;

namespace Trax.Dashboard.Components.Layout.Sidebar;

public partial class DashboardSidebar
{
    [Parameter]
    public bool Expanded { get; set; } = true;

    private bool _dataExpanded = true;
    private bool _settingsExpanded = true;
}
