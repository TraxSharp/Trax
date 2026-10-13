using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.StateMachine.E2E.Fakes;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Api.StateMachine.E2E;

/// <summary>
/// Starts <see cref="E2EHost"/>s that serve <see cref="ShelfMachine"/> in a chosen shape, so two hosts over one
/// database serve two versions of the machine, as the nodes of a rolling deploy do.
/// </summary>
public static class ShelfHosts
{
    /// <summary>
    /// Starts a host on <paramref name="database"/> whose <see cref="ShelfMachine"/> is built as
    /// <paramref name="shape"/>, exposing it (and the wizard) through the typed surface, with the machine documents
    /// persisted. The shape is put back to <see cref="ShelfShape.V1"/> before this returns, once the host has built
    /// its machines.
    /// </summary>
    public static async Task<IHost> StartAsync(
        string database,
        ShelfShape shape,
        string applicationName,
        E2EHostOptions? options = null
    )
    {
        options ??= new E2EHostOptions();
        ShelfMachine.Shape = shape;
        try
        {
            var host = await E2EHost.StartAsync(
                database,
                new CountingCharge(),
                options with
                {
                    ApplicationName = applicationName,
                    Served = options.Served.Count > 0 ? options.Served : Served,
                    GraphQL = options.GraphQL ?? MachineDocumentOps.Persisted(),
                }
            );
            // The machines are singletons the container builds on first use; build them under this shape.
            _ = host.Services.GetServices<IMachine>().ToList();
            return host;
        }
        finally
        {
            ShelfMachine.Shape = ShelfShape.V1;
        }
    }

    /// <summary>The machines a shelf host serves unless its options name others: the shelf and the wizard.</summary>
    public static IReadOnlyList<Type> Served { get; } =
    [typeof(ShelfMachine), typeof(WizardMachine)];

    /// <summary>The <c>start</c> mutation of the shelf.</summary>
    public static string Start(string label, string requestId) =>
        $"mutation {{ machines {{ shelf {{ start(input: {{ label: {WizardOps.Literal(label)} }}, requestId: {WizardOps.Literal(requestId)}) {{ id revision state problem {{ code message }} }} }} }} }}";

    /// <summary>The mutation of the shelf event whose field is <paramref name="field"/>.</summary>
    public static string Event(string field, Guid id, string requestId) =>
        $"mutation {{ machines {{ shelf {{ {field}(id: {WizardOps.Literal(id.ToString())}, requestId: {WizardOps.Literal(requestId)}) {{ id revision state problem {{ code message }} }} }} }} }}";

    /// <summary>Starts a shelf as <paramref name="apiKey"/>'s user and returns its id. Fails the test on a refusal.</summary>
    public static async Task<Guid> StartShelfAsync(this IHost host, string apiKey)
    {
        using var doc = await host.PostAsync(
            Start("Atlas", WizardOps.NewRequestId()),
            apiKey: apiKey
        );
        return ReadId(doc, "start");
    }

    /// <summary>Sends a shelf event inline and returns the answer.</summary>
    public static Task<JsonDocument> SendShelfEventAsync(
        this IHost host,
        string apiKey,
        string field,
        Guid id
    ) => host.PostAsync(Event(field, id, WizardOps.NewRequestId()), apiKey: apiKey);

    /// <summary>The instance id <c>machines.shelf.&lt;field&gt;</c> answered. Fails the test on errors or a problem.</summary>
    public static Guid ReadId(JsonDocument doc, string field)
    {
        if (!MachineDocumentOps.Succeeded(doc, ShelfMachine.MachineId, field))
            Assert.Fail($"machines.shelf.{field} was refused: {MachineDocumentOps.Show(doc)}");
        return Guid.Parse(
            doc.RootElement.GetProperty("data")
                .GetProperty("machines")
                .GetProperty("shelf")
                .GetProperty(field)
                .GetProperty("id")
                .GetString()!
        );
    }

    /// <summary>Stops a host and disposes it, bounded.</summary>
    public static async Task StopAsync(IHost? host)
    {
        if (host is null)
            return;
        try
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await host.StopAsync(stop.Token);
        }
        catch (OperationCanceledException) { }
        host.Dispose();
    }
}
