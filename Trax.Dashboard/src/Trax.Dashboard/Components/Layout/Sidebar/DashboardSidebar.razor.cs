using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.PersistedOperations;

namespace Trax.Dashboard.Components.Layout.Sidebar;

/// <summary>
/// The dashboard's navigation menu, with links built from <c>DashboardOptions.RoutePrefix</c>.
/// The persisted operations link appears only when the host registers Trax.Api's persisted
/// operations capability. Part of the dashboard UI; not intended to be used directly.
/// </summary>
public partial class DashboardSidebar
{
    /// <summary>Whether the menu shows labels (expanded) or icons only.</summary>
    [Parameter]
    public bool Expanded { get; set; } = true;

    [Inject]
    private IServiceProvider Services { get; set; } = default!;

    private bool _dataExpanded = true;
    private bool _settingsExpanded = true;
    private bool _persistedOperationsAvailable;

    /// <summary>
    /// Checks once whether <c>IPersistedOperationsCapability</c> is registered, which decides
    /// whether the persisted operations link is shown.
    /// </summary>
    protected override void OnInitialized()
    {
        _persistedOperationsAvailable =
            Services.GetService<IPersistedOperationsCapability>() is not null;
    }
}
