using Microsoft.AspNetCore.Components;
using Radzen;
using Trax.Dashboard.Utilities;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrustedExecution;
using Trax.Scheduler.Services.Operations;

namespace Trax.Dashboard.Components.Dialogs;

/// <summary>
/// Dialog that runs a train now with input entered through a generated form or as raw JSON,
/// opened from the Trains page. It runs through the scheduler's <c>IOperationsService</c> inside
/// the <c>"dashboard"</c> trusted execution scope, the path the API's run takes: the service reads
/// the input, writes the run's row, applies the train's queue hook and submits to the job
/// submitter the train is routed to. No work queue entry is created, so the train's subject key is
/// not consulted; for a subject-keyed train the dialog warns about this. On success it navigates
/// to the new run. Opened by the dashboard's own pages through Radzen's <c>DialogService</c>; not
/// intended to be used directly.
/// </summary>
public partial class RunTrainDialog : IDisposable
{
    private readonly CancellationTokenSource _cts = new();

    [Inject]
    private ITrustedExecutionScope TrustedScope { get; set; } = default!;

    [Inject]
    private IOperationsService OperationsService { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private DialogService DialogService { get; set; } = default!;

    /// <summary>
    /// The train to run, from train discovery. Its input type drives the form, and its service
    /// type's FullName is the train name sent to the operations service.
    /// </summary>
    [Parameter]
    public required TrainRegistration Registration { get; set; }

    private int _selectedTab;
    private string _jsonInput = "";
    private string? _error;
    private bool _running;

    private TrainInputForm _form = null!;

    /// <summary>
    /// Builds one form field per public readable property of the train's input type.
    /// </summary>
    protected override void OnInitialized() => _form = new TrainInputForm(Registration.InputType);

    private async Task EnqueueTrain()
    {
        _error = null;
        _running = true;

        try
        {
            // The JSON tab's text goes to the service as typed: it reads property names in any
            // case and refuses one given twice (docs/0023), as for every caller.
            string inputJson;
            if (_selectedTab != 0)
                inputJson = _jsonInput;
            else if (!_form.TryBuildJson(out inputJson))
            {
                _error = _form.ErrorSummary();
                return;
            }

            OperationResult result;
            // The dashboard is the admin surface, gated as a whole by its host, so it runs as
            // trusted infrastructure rather than as a user a train's [TraxAuthorize] can check.
            // The trusted scope is also what lets a subject-keyed train be run now at all.
            // See docs/0017 and docs/0022.
            using (TrustedScope.BeginTrusted("dashboard"))
                result = await OperationsService.RunTrainAsync(
                    new RunTrainInput(Registration.ServiceType.FullName!, inputJson),
                    _cts.Token
                );

            if (!result.Success)
            {
                _error = result.Message;
                return;
            }

            DialogService.Close();
            if (result.Id is { } id)
                Navigation.NavigateTo($"trax/data/metadata/{id}");
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
        finally
        {
            _running = false;
        }
    }

    /// <summary>
    /// Cancels a run request still in flight when the dialog closes.
    /// </summary>
    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
