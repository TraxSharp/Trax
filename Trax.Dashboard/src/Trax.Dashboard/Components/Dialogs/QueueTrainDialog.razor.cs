using Microsoft.AspNetCore.Components;
using Radzen;
using Trax.Dashboard.Utilities;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrustedExecution;
using Trax.Scheduler.Services.Operations;

namespace Trax.Dashboard.Components.Dialogs;

/// <summary>
/// Dialog that queues a train with input entered through a generated form or as raw JSON, opened
/// from the Trains page. It enqueues through the scheduler's <c>IOperationsService</c> inside the
/// <c>"dashboard"</c> trusted execution scope, so the train's queue hooks, subject key and input
/// size cap apply but per-train authorization does not, and on success navigates to the new work
/// queue entry. Opened by the dashboard's own pages through Radzen's <c>DialogService</c>; not intended to be used directly.
/// </summary>
public partial class QueueTrainDialog : IDisposable
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
    /// The train to queue, from train discovery. Its input type drives the form, and its service
    /// type's FullName is the train name sent to the operations service.
    /// </summary>
    [Parameter]
    public required TrainRegistration Registration { get; set; }

    private int _selectedTab;
    private string _jsonInput = "";
    private string? _error;
    private bool _running;
    private int _priority;

    private TrainInputForm _form = null!;

    /// <summary>
    /// Builds one form field per public readable property of the train's input type.
    /// </summary>
    protected override void OnInitialized() => _form = new TrainInputForm(Registration.InputType);

    private async Task QueueTrain()
    {
        _error = null;
        _running = true;

        try
        {
            // The form tab's fields are read in the invariant culture, and a field that does not
            // read as its type is refused here. Either tab ends as a JSON string handed to the
            // shared IOperationsService, which reads and validates the input.
            string inputJson;
            if (_selectedTab != 0)
                inputJson = _jsonInput;
            else if (!_form.TryBuildJson(out inputJson))
            {
                _error = _form.ErrorSummary();
                return;
            }

            OperationResult result;
            // The dashboard is the admin surface, gated as a whole by its host, so it enqueues as
            // trusted infrastructure rather than as a user a train's [TraxAuthorize] can check: a
            // Blazor circuit has no request to carry one. OnQueue, the subject key and the input
            // cap still apply. See docs/0017.
            using (TrustedScope.BeginTrusted("dashboard"))
                result = await OperationsService.QueueTrainAsync(
                    new QueueTrainInput(
                        TrainName: Registration.ServiceType.FullName!,
                        InputJson: inputJson,
                        Priority: _priority
                    ),
                    _cts.Token
                );

            if (!result.Success)
            {
                _error = result.Message;
                return;
            }

            DialogService.Close();
            if (result.Id is { } id)
                Navigation.NavigateTo($"trax/data/work-queue/{id}");
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
    /// Cancels a queue request still in flight when the dialog closes.
    /// </summary>
    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
