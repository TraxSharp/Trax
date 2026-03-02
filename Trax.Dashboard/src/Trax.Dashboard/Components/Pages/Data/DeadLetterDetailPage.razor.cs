using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Radzen;
using Trax.Dashboard.Services.TrainDiscovery;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Utils;
using static Trax.Dashboard.Utilities.DashboardFormatters;

namespace Trax.Dashboard.Components.Pages.Data;

public partial class DeadLetterDetailPage
{
    [Inject]
    private IDataContextProviderFactory DataContextFactory { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private ITrainDiscoveryService TrainDiscovery { get; set; } = default!;

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
        if (_deadLetter?.Manifest is null)
            return;

        _actionError = null;
        _requeueing = true;

        try
        {
            var manifest = _deadLetter.Manifest;

            var registration = TrainDiscovery
                .DiscoverTrains()
                .FirstOrDefault(r =>
                    r.ServiceType.FullName == manifest.Name
                    || r.ImplementationType.FullName == manifest.Name
                );

            if (registration is null)
            {
                _actionError =
                    $"No train registration found for '{ShortName(manifest.Name)}'. Is the train still registered?";
                return;
            }

            string? serializedInput = null;
            string? inputTypeName = null;

            if (!string.IsNullOrWhiteSpace(manifest.Properties))
            {
                var deserializedInput = JsonSerializer.Deserialize(
                    manifest.Properties,
                    registration.InputType,
                    TraxJsonSerializationOptions.ManifestProperties
                );

                if (deserializedInput is not null)
                {
                    serializedInput = JsonSerializer.Serialize(
                        deserializedInput,
                        registration.InputType,
                        TraxJsonSerializationOptions.ManifestProperties
                    );
                    inputTypeName = registration.InputType.FullName;
                }
            }

            var entry = WorkQueue.Create(
                new CreateWorkQueue
                {
                    TrainName = manifest.Name,
                    Input = serializedInput,
                    InputTypeName = inputTypeName,
                    ManifestId = manifest.Id,
                    Priority = manifest.Priority,
                }
            );

            using var dataContext = await DataContextFactory.CreateDbContextAsync(DisposalToken);

            await dataContext.Track(entry);

            // Only update the dead letter record when it's still blocking the ManifestManager.
            // Already-resolved dead letters (Retried/Acknowledged) are just audit records —
            // re-queuing from them creates a fresh WorkQueue entry without mutating history.
            if (_deadLetter.Status == DeadLetterStatus.AwaitingIntervention)
            {
                var trackedDeadLetter = await dataContext.DeadLetters.FirstAsync(d =>
                    d.Id == DeadLetterId
                );

                trackedDeadLetter.Status = DeadLetterStatus.Retried;
                trackedDeadLetter.ResolvedAt = DateTime.UtcNow;
                trackedDeadLetter.ResolutionNote =
                    $"Re-queued via dashboard (WorkQueue {entry.Id})";
            }

            await dataContext.SaveChanges(DisposalToken);

            NotificationService.Notify(
                NotificationSeverity.Success,
                "Train Re-queued",
                $"{ShortName(manifest.Name)} has been re-queued (WorkQueue ID {entry.Id}).",
                duration: 4000
            );

            Navigation.NavigateTo($"trax/data/work-queue/{entry.Id}");
        }
        catch (JsonException je)
        {
            _actionError = $"Invalid manifest properties JSON: {je.Message}";
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
            using var dataContext = await DataContextFactory.CreateDbContextAsync(DisposalToken);

            var trackedDeadLetter = await dataContext.DeadLetters.FirstAsync(d =>
                d.Id == DeadLetterId
            );

            trackedDeadLetter.Acknowledge(_acknowledgeNote);
            await dataContext.SaveChanges(DisposalToken);

            _showAcknowledgeInput = false;
            _acknowledgeNote = "";

            NotificationService.Notify(
                NotificationSeverity.Success,
                "Dead Letter Acknowledged",
                $"Dead letter #{DeadLetterId} has been acknowledged.",
                duration: 4000
            );

            // Reload to reflect updated status
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
