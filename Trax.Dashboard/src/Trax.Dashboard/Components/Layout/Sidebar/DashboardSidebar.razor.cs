using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.PersistedOperations;

namespace Trax.Dashboard.Components.Layout.Sidebar;

public partial class DashboardSidebar
{
    [Parameter]
    public bool Expanded { get; set; } = true;

    [Inject]
    private IServiceProvider Services { get; set; } = default!;

    private bool _dataExpanded = true;
    private bool _settingsExpanded = true;
    private bool _persistedOperationsAvailable;

    protected override void OnInitialized()
    {
        _persistedOperationsAvailable =
            Services.GetService<IPersistedOperationsCapability>() is not null;
    }
}
