using Trax.Dashboard.Components.Shared;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models.WorkQueue;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Radzen;
using static Trax.Dashboard.Utilities.DashboardFormatters;

namespace Trax.Dashboard.Components.Pages.Data;

public partial class WorkQueueDetailPage
{
    [Inject]
    private IDataContextProviderFactory DataContextFactory { get; set; } = default!;

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
            using var context = await DataContextFactory.CreateDbContextAsync(DisposalToken);
            var entry = await context.WorkQueues.FirstOrDefaultAsync(q => q.Id == WorkQueueId);

            if (entry is null)
            {
                _error = "Work queue entry not found.";
                return;
            }

            if (entry.Status != WorkQueueStatus.Queued)
            {
                _error = $"Cannot cancel entry with status '{entry.Status}'.";
                return;
            }

            entry.Status = WorkQueueStatus.Cancelled;
            await context.SaveChanges(DisposalToken);

            _entry = entry;

            NotificationService.Notify(
                NotificationSeverity.Success,
                "Entry Cancelled",
                $"Work queue entry {entry.Id} has been cancelled.",
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

    private static BadgeStyle GetStatusBadgeStyle(WorkQueueStatus status) =>
        status switch
        {
            WorkQueueStatus.Queued => BadgeStyle.Info,
            WorkQueueStatus.Dispatched => BadgeStyle.Success,
            WorkQueueStatus.Cancelled => BadgeStyle.Warning,
            _ => BadgeStyle.Light,
        };
}
