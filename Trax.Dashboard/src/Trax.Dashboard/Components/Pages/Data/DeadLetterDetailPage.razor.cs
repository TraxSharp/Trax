using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Radzen;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Models.Metadata;
using Trax.Scheduler.Services.TraxScheduler;
using static Trax.Dashboard.Utilities.DashboardFormatters;

namespace Trax.Dashboard.Components.Pages.Data;

public partial class DeadLetterDetailPage
{
    [Inject]
    private IDataContextProviderFactory DataContextFactory { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private ITraxScheduler Scheduler { get; set; } = default!;

    [Inject]
    private NotificationService NotificationService { get; set; } = default!;

    [Parameter]
    public long DeadLetterId { get; set; }

    private DeadLetter? _deadLetter;
    private List<Metadata> _failedRuns = [];
    private Metadata? _latestFailedRun;

    private bool _requeueing;
    private bool _acknowledging;
    private bool _showAcknowledgeInput;
    private string _acknowledgeNote = "";
    private string? _actionError;

    protected override object? GetRouteKey() => DeadLetterId;

    protected override async Task LoadDataAsync(CancellationToken cancellationToken)
    {
        using var context = await DataContextFactory.CreateDbContextAsync(cancellationToken);

        _deadLetter = await context
            .DeadLetters.Include(d => d.Manifest)
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == DeadLetterId, cancellationToken);

        if (_deadLetter is not null)
        {
            _failedRuns = await context
                .Metadatas.AsNoTracking()
                .Where(m =>
                    m.ManifestId == _deadLetter.ManifestId && m.TrainState == TrainState.Failed
                )
                .OrderByDescending(m => m.StartTime)
                .ToListAsync(cancellationToken);

            _latestFailedRun = _failedRuns.FirstOrDefault();
        }
    }

    private async Task RequeueManifest()
    {
        if (_deadLetter is null)
            return;

        _actionError = null;
        _requeueing = true;

        try
        {
            var result = await Scheduler.RequeueDeadLetterAsync(DeadLetterId, DisposalToken);

            if (!result.Success)
            {
                _actionError = result.Message;
                return;
            }

            NotificationService.Notify(
                NotificationSeverity.Success,
                "Train Re-queued",
                $"{ShortName(_deadLetter.Manifest?.Name ?? "Unknown")} has been re-queued (WorkQueue ID {result.WorkQueueId}).",
                duration: 4000
            );

            Navigation.NavigateTo($"trax/data/work-queue/{result.WorkQueueId}");
        }
        catch (Exception ex)
        {
            _actionError = ex.Message;
        }
        finally
        {
            _requeueing = false;
        }
    }

    private async Task AcknowledgeDeadLetter()
    {
        if (_deadLetter is null)
            return;

        _actionError = null;
        _acknowledging = true;

        try
        {
            var result = await Scheduler.AcknowledgeDeadLetterAsync(
                DeadLetterId,
                _acknowledgeNote,
                DisposalToken
            );

            if (!result.Success)
            {
                _actionError = result.Message;
                return;
            }

            _showAcknowledgeInput = false;
            _acknowledgeNote = "";

            NotificationService.Notify(
                NotificationSeverity.Success,
                "Dead Letter Acknowledged",
                $"Dead letter #{DeadLetterId} has been acknowledged.",
                duration: 4000
            );

            await LoadDataAsync(DisposalToken);
        }
        catch (Exception ex)
        {
            _actionError = ex.Message;
        }
        finally
        {
            _acknowledging = false;
        }
    }
}
