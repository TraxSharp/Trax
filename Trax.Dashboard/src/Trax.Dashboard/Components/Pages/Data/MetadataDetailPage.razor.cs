using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Trax.Dashboard.Components.Shared;
using Trax.Dashboard.Models;
using Trax.Dashboard.Utilities;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models.Log;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Utils;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrustedExecution;
using Trax.Scheduler.Services.Operations;
using static Trax.Dashboard.Utilities.DashboardFormatters;

namespace Trax.Dashboard.Components.Pages.Data;

/// <summary>
/// The page for one run (metadata row), at <c>/trax/data/metadata/{id}</c>: its state, input,
/// output, failure details and a paged grid of its logs. The user can cancel it while it is in
/// progress, or queue the train again with the run's saved input. Part of the dashboard UI, routed by the package; not intended to be used directly.
/// </summary>
public partial class MetadataDetailPage
{
    [Inject]
    private ITrustedExecutionScope TrustedScope { get; set; } = default!;

    [Inject]
    private IDataContextProviderFactory DataContextFactory { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private ITrainDiscoveryService TrainDiscovery { get; set; } = default!;

    [Inject]
    private NotificationService NotificationService { get; set; } = default!;

    [Inject]
    private IOperationsService OperationsService { get; set; } = default!;

    [Inject]
    private IServiceProvider ServiceProvider { get; set; } = default!;

    /// <summary>The run's (metadata row's) database id, from the route.</summary>
    [Parameter]
    public long MetadataId { get; set; }

    private Metadata? _metadata;
    private int _logCount;
    private TraxDataGrid<Log>? _logsGrid;

    private Task<ServerDataResult<Log>> LoadLogsPageAsync(
        LoadDataArgs args,
        CancellationToken ct
    ) =>
        DataGridQueryHelper.LoadPageAsync(
            DataContextFactory,
            db => db.Logs.AsNoTracking().Where(l => l.MetadataId == MetadataId).OrderBy(l => l.Id),
            args,
            ct
        );

    private bool _rerunning;
    private string? _rerunError;
    private bool _cancelling;
    private string? _cancelError;

    /// <inheritdoc/>
    /// <remarks>Returns <see cref="MetadataId"/>.</remarks>
    private protected override object? GetRouteKey() => MetadataId;

    /// <summary>
    /// Loads the run and the number of its log entries, and reloads the logs grid, which pages its
    /// rows from the database. Leaves the page empty when no run has the id.
    /// </summary>
    /// <param name="cancellationToken">Cancelled when the page is disposed or a newer load starts.</param>
    private protected override async Task LoadDataAsync(CancellationToken cancellationToken)
    {
        using var context = await DataContextFactory.CreateDbContextAsync(cancellationToken);

        _metadata = await context
            .Metadatas.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == MetadataId, cancellationToken);

        if (_metadata is not null)
        {
            // The grid pages its logs from the database, as the API's logs query does; the
            // page only needs to know whether there are any.
            _logCount = await context
                .Logs.AsNoTracking()
                .CountAsync(l => l.MetadataId == MetadataId, cancellationToken);

            if (_logsGrid is not null)
                await _logsGrid.ReloadAsync();
        }
    }

    private async Task CancelTrain()
    {
        if (_metadata is null || _metadata.TrainState != TrainState.InProgress)
            return;

        _cancelError = null;
        _cancelling = true;

        try
        {
            await CancellationHelper.CancelTrainsAsync(
                DataContextFactory,
                ServiceProvider,
                [MetadataId],
                DisposalToken
            );

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

    private async Task RequeueTrain()
    {
        if (_metadata is null || string.IsNullOrWhiteSpace(_metadata.Input))
            return;

        _rerunError = null;
        _rerunning = true;

        try
        {
            var registration = TrainDiscovery
                .DiscoverTrains()
                .FirstOrDefault(r => r.ServiceType.FullName == _metadata.Name);

            if (registration is null)
            {
                _rerunError =
                    $"No train registration found for '{ShortName(_metadata.Name)}'. Is the train still registered?";
                return;
            }

            // Parse the saved input to check it still fits the train before queueing it again.
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

            // In the form the mediator reads, which is not necessarily the one the input was
            // saved in.
            var inputJson = JsonSerializer.Serialize(
                deserializedInput,
                registration.InputType,
                TraxEffectConfiguration.StaticSystemJsonSerializerOptions
            );

            // Through the operations service, which enqueues through the mediator, so the
            // train's OnQueue hook, subject key and input cap apply to a re-queue as they do to
            // any other enqueue. Writing the row here skipped them.
            OperationResult result;
            // The dashboard is the admin surface, gated as a whole by its host, so it enqueues as
            // trusted infrastructure rather than as a user a train's [TraxAuthorize] can check: a
            // Blazor circuit has no request to carry one. OnQueue, the subject key and the input
            // cap still apply. See docs/0017.
            using (TrustedScope.BeginTrusted("dashboard"))
                result = await OperationsService.QueueTrainAsync(
                    new QueueTrainInput(TrainName: _metadata.Name, InputJson: inputJson),
                    DisposalToken
                );

            if (!result.Success || result.Id is not { } entryId)
            {
                _rerunError = result.Message;
                return;
            }

            NotificationService.Notify(
                NotificationSeverity.Success,
                "Train Queued",
                $"{ShortName(_metadata.Name)} has been re-queued (ID {entryId}).",
                duration: 4000
            );

            Navigation.NavigateTo($"trax/data/work-queue/{entryId}");
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
