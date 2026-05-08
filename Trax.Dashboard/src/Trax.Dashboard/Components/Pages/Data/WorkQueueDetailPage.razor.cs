using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Radzen;
using Trax.Dashboard.Components.Shared;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Models.WorkQueue;
using Trax.Scheduler.Services.Operations;
using static Trax.Dashboard.Utilities.DashboardFormatters;

namespace Trax.Dashboard.Components.Pages.Data;

public partial class WorkQueueDetailPage
{
    [Inject]
    private IDataContextProviderFactory DataContextFactory { get; set; } = default!;

    [Inject]
    private IOperationsService OperationsService { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private NotificationService NotificationService { get; set; } = default!;

    [Parameter]
    public long WorkQueueId { get; set; }

    private WorkQueue? _entry;
    private bool _cancelling;
    private string? _error;

    protected override object? GetRouteKey() => WorkQueueId;

    protected override async Task LoadDataAsync(CancellationToken cancellationToken)
    {
        using var context = await DataContextFactory.CreateDbContextAsync(cancellationToken);
        _entry = await context
            .WorkQueues.AsNoTracking()
            .FirstOrDefaultAsync(q => q.Id == WorkQueueId, cancellationToken);
    }

    private async Task CancelEntry()
    {
        if (_entry is null)
            return;

        _error = null;
        _cancelling = true;

        try
        {
            var result = await OperationsService.CancelWorkQueueEntryAsync(
                WorkQueueId,
                DisposalToken
            );

            if (!result.Success)
            {
                _error = result.Message;
                return;
            }

            // Reload so the UI reflects the new status without a full page navigation.
            await LoadDataAsync(DisposalToken);

            NotificationService.Notify(
                NotificationSeverity.Success,
                "Entry Cancelled",
                $"Work queue entry {WorkQueueId} has been cancelled.",
                duration: 4000
            );
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
        finally
        {
            _cancelling = false;
        }
    }
}
