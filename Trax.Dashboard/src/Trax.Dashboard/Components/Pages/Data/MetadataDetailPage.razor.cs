using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Trax.Dashboard.Services.WorkflowDiscovery;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models.Log;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Utils;
using Trax.Scheduler.Services.CancellationRegistry;
using static Trax.Dashboard.Utilities.DashboardFormatters;

namespace Trax.Dashboard.Components.Pages.Data;

public partial class MetadataDetailPage
{
    [Inject]
    private IDataContextProviderFactory DataContextFactory { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private IWorkflowDiscoveryService WorkflowDiscovery { get; set; } = default!;

    [Inject]
    private NotificationService NotificationService { get; set; } = default!;

    [Inject]
    private IServiceProvider ServiceProvider { get; set; } = default!;

    [Parameter]
    public long MetadataId { get; set; }

    private Metadata? _metadata;
    private List<Log> _logs = [];
    private bool _rerunning;
    private string? _rerunError;
    private bool _cancelling;
    private string? _cancelError;

    protected override object? GetRouteKey() => MetadataId;

    protected override async Task LoadDataAsync(CancellationToken cancellationToken)
    {
        using var context = await DataContextFactory.CreateDbContextAsync(cancellationToken);

        _metadata = await context
            .Metadatas.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == MetadataId, cancellationToken);

        if (_metadata is not null)
        {
            _logs = await context
                .Logs.AsNoTracking()
                .Where(l => l.MetadataId == MetadataId)
                .ToListAsync(cancellationToken);
        }
    }

    private async Task CancelWorkflow()
    {
        if (_metadata is null || _metadata.WorkflowState != WorkflowState.InProgress)
            return;

        _cancelError = null;
        _cancelling = true;

        try
        {
            // Always set DB flag (works cross-server)
            using var context = await DataContextFactory.CreateDbContextAsync(DisposalToken);
            await context
                .Metadatas.Where(m => m.Id == MetadataId)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(m => m.CancellationRequested, true),
                    DisposalToken
                );

            // Same-server instant cancel bonus
            ServiceProvider.GetService<ICancellationRegistry>()?.TryCancel(MetadataId);

            NotificationService.Notify(
                NotificationSeverity.Success,
                "Cancellation Requested",
                $"Cancel signal sent for {ShortName(_metadata.Name)}.",
                duration: 4000
            );
        }
        catch (Exception ex)
        {
            _cancelError = ex.Message;
        }
        finally
        {
            _cancelling = false;
        }
    }

    private async Task RequeueWorkflow()
    {
        if (_metadata is null || string.IsNullOrWhiteSpace(_metadata.Input))
            return;

        _rerunError = null;
        _rerunning = true;

        try
        {
            var registration = WorkflowDiscovery
                .DiscoverWorkflows()
                .FirstOrDefault(r =>
                    r.ServiceType.FullName == _metadata.Name
                    || r.ImplementationType.FullName == _metadata.Name
                );

            if (registration is null)
            {
                _rerunError =
                    $"No workflow registration found for '{ShortName(_metadata.Name)}'. Is the workflow still registered?";
                return;
            }

            // Re-serialize the input using ManifestProperties options for clean JSON
            var deserializedInput = JsonSerializer.Deserialize(
                _metadata.Input,
                registration.InputType,
                TraxJsonSerializationOptions.ManifestProperties
            );

            if (deserializedInput is null)
            {
                _rerunError = "Failed to deserialize the saved input.";
                return;
            }

            var serializedInput = JsonSerializer.Serialize(
                deserializedInput,
                registration.InputType,
                TraxJsonSerializationOptions.ManifestProperties
            );

            var entry = WorkQueue.Create(
                new CreateWorkQueue
                {
                    WorkflowName = _metadata.Name,
                    Input = serializedInput,
                    InputTypeName = registration.InputType.FullName,
                }
            );

            using var dataContext = await DataContextFactory.CreateDbContextAsync(DisposalToken);
            await dataContext.Track(entry);
            await dataContext.SaveChanges(DisposalToken);

            NotificationService.Notify(
                NotificationSeverity.Success,
                "Workflow Queued",
                $"{ShortName(_metadata.Name)} has been re-queued (ID {entry.Id}).",
                duration: 4000
            );

            Navigation.NavigateTo($"trax/data/work-queue/{entry.Id}");
        }
        catch (JsonException je)
        {
            _rerunError = $"Invalid saved input JSON: {je.Message}";
        }
        catch (Exception ex)
        {
            _rerunError = ex.Message;
        }
        finally
        {
            _rerunning = false;
        }
    }
}
