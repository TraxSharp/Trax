using System.Diagnostics;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Api.GraphQL.Configuration;
using Trax.Api.GraphQL.PersistedOperations.Configuration;
using Trax.Api.GraphQL.PersistedOperations.Services;
using Trax.Api.GraphQL.PersistedOperations.Storage;
using Trax.Api.GraphQL.TypeModules;
using Trax.Effect.StateMachine.Persistence;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Api.GraphQL.PersistedOperations.Startup;

/// <summary>
/// Generates every exposed machine's documents from the built schema and registers them in the store before the host
/// serves anything, so the documents a client was built with are there on the first request and a request that
/// arrives while the host starts is never answered from a cached miss. While the host runs, it registers them again
/// every <see cref="PersistedOperationsOptions.MachineDocumentCollectionInterval"/> (or a quarter of the grace
/// period, if that is shorter), which keeps its own documents marked as registered and collects any other version's
/// that no node has registered for the grace period, without waiting for a node to restart. A node that serves no
/// machine registers an empty set the same way, so the documents of a deploy that stopped serving every machine are
/// still marked and collected, and their clients reload instead of retrying forever.
/// </summary>
/// <remarks>
/// It registers in <see cref="StartingAsync"/>, which the host finishes for every hosted service before it calls any
/// <c>StartAsync</c>, so before Kestrel binds and before a hosted service that runs a request can. A harness that
/// calls only <c>StartAsync</c> gets the same registration from it. A failure there stops the host: a node that cannot
/// register its documents cannot serve its machines. A later registration that fails is logged and tried again at the
/// next interval; the node keeps serving what it registered. A node that serves no machine is not stopped by a failed
/// registration: it logs it and tries again at the next interval. Registering is idempotent and runs under one lock
/// shared by every node, so every node may run it. See
/// <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.
/// </remarks>
internal sealed class MachineDocumentRegistration(
    IServiceProvider services,
    MachineDocumentRegistry registry,
    DbPersistedOperationStorage storage,
    PersistedOperationsOptions options,
    ILogger<MachineDocumentRegistration>? logger = null
) : IHostedLifecycleService, IDisposable
{
    private const string TraxSchemaName = "trax";

    private readonly ILogger _logger = logger ?? NullLogger<MachineDocumentRegistration>.Instance;
    private readonly CancellationTokenSource _stopping = new();
    private bool _registered;
    private bool _consoleIncluded;
    private IReadOnlyList<GeneratedDocument> _documents = [];
    private Task? _collecting;

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        _registered = true;

        var exposed = MachineSurface.Exposed(
            services.GetServices<IMachine>(),
            services.GetService<StateMachineOptions>()
        );
        var trains = services.GetService<ITrainDiscoveryService>() is { } discovery
            ? TrainTypeModule.AssignTrainNames(discovery.DiscoverTrains())
            : new Dictionary<TrainRegistration, string>();
        var graphQL = services.GetService<GraphQLConfiguration>();
        var console = graphQL?.ConsoleDocuments ?? [];
        if (
            exposed.Count == 0
            && trains.Count == 0
            && options.ClientDocumentFiles.Count == 0
            && console.Count == 0
        )
        {
            // No document of this node's, but the registration still runs: it marks the documents of every machine no
            // node serves, and collects them after the grace period, so an earlier deploy's clients reload.
            registry.Publish(
                [],
                new Dictionary<string, int>(),
                new Dictionary<string, StoredMachineDocument>()
            );
            try
            {
                await RegisterAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    ex,
                    "Could not register this node's empty set of machine documents; the documents of machines no node "
                        + "serves are collected at the next attempt, in {Interval}.",
                    Interval
                );
            }
            return;
        }

        var executor = await services
            .GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync(TraxSchemaName, cancellationToken)
            .ConfigureAwait(false);
        _documents =
        [
            .. (exposed.Count == 0 ? [] : MachineDocuments.Generate(executor.Schema, exposed)),
            .. ProgressDocuments.Generate(executor.Schema),
        ];
        var clientDocuments = LoadClientDocuments();
        // What a client document may reach, decided from what the host registered: the same reach judges the stored
        // documents other nodes registered (MachineDocumentMiddleware).
        var reach = ClientDocumentReach.Of(
            executor.Schema,
            trains,
            graphQL?.ModelRegistrations ?? []
        );
        registry.ClientReach = reach;
        Services.ClientDocuments.Check(executor.Schema, reach, clientDocuments);
        _documents = [.. _documents, .. clientDocuments];
        GeneratedDocumentValidation.Validate(executor.Schema, _documents);

        IncludeConsoleDocuments(executor.Schema, console);
        await RegisterAsync(cancellationToken).ConfigureAwait(false);
        PublishConsoleDocumentIds();
    }

    // The operations console's documents, when the host serves it. The console reaches optional parts of the schema
    // (the operations mutations, junction events, machines), so a document this host cannot serve is left out, and
    // the console sends it as text, which persisted operations then refuse, rather than refusing the start.
    private void IncludeConsoleDocuments(
        HotChocolate.ISchemaDefinition schema,
        IReadOnlyList<Trax.Api.GraphQL.Configuration.ConsoleDocumentText> console
    )
    {
        if (console.Count == 0)
            return;
        var (served, skipped) = ConsoleDocuments.Select(schema, console);
        foreach (var reason in skipped)
            _logger.LogInformation(
                "The operations console's document {Reason} The console cannot use it on this host.",
                reason
            );
        _documents = [.. _documents, .. served];
        _consoleIncluded = true;
    }

    // Tells the console's page the id of each of its documents this node registered.
    private void PublishConsoleDocumentIds()
    {
        if (services.GetService<GraphQLConfiguration>() is { } graphQL)
            graphQL.ConsoleDocumentIds = _documents
                .OfType<ConsoleDocument>()
                .ToDictionary(d => d.Name, d => d.Id, StringComparer.Ordinal);
    }

    // Every client document the host names, each file resolved against the content root; a document's name used twice
    // across files refuses the start.
    private IReadOnlyList<ClientDocument> LoadClientDocuments()
    {
        var root =
            services.GetService<IHostEnvironment>()?.ContentRootPath
            ?? Directory.GetCurrentDirectory();
        var documents = new List<ClientDocument>();
        foreach (var file in options.ClientDocumentFiles)
        foreach (var document in Services.ClientDocuments.Load(Path.GetFullPath(file, root)))
        {
            if (documents.Any(d => d.Name == document.Name))
                throw new InvalidOperationException(
                    $"The {document.Owner} is named in two client documents files. Operation names are unique "
                        + "across a host's frontends."
                );
            documents.Add(document);
        }
        return documents;
    }

    public Task StartAsync(CancellationToken cancellationToken) =>
        _registered ? Task.CompletedTask : StartingAsync(cancellationToken);

    public async Task StartedAsync(CancellationToken cancellationToken)
    {
        // A host that maps the console in a Startup's Configure, which runs while the web host starts, hands over the
        // console's documents only after StartingAsync. They are registered now, once every endpoint is mapped.
        if (
            !_consoleIncluded
            && services.GetService<GraphQLConfiguration>()?.ConsoleDocuments
                is { Count: > 0 } console
        )
        {
            var executor = await services
                .GetRequiredService<IRequestExecutorProvider>()
                .GetExecutorAsync(TraxSchemaName, cancellationToken)
                .ConfigureAwait(false);
            IncludeConsoleDocuments(executor.Schema, console);
            await RegisterAsync(cancellationToken).ConfigureAwait(false);
            PublishConsoleDocumentIds();
        }
        if (_collecting is null)
            _collecting = Task.Run(() => CollectAsync(_stopping.Token));
    }

    public async Task StoppingAsync(CancellationToken cancellationToken)
    {
        await _stopping.CancelAsync().ConfigureAwait(false);
        if (_collecting is { } collecting)
            await collecting.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken) => StoppingAsync(cancellationToken);

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose()
    {
        _stopping.Cancel();
        _stopping.Dispose();
    }

    private async Task RegisterAsync(CancellationToken cancellationToken)
    {
        var readStartedAt = Stopwatch.GetTimestamp();
        var (ownVersions, stored) = await storage
            .RegisterMachineDocumentsAsync(
                _documents,
                options.MachineDocumentGracePeriod,
                cancellationToken
            )
            .ConfigureAwait(false);
        registry.Publish(_documents, ownVersions, stored, readStartedAt);
    }

    // A node writes its documents' updated_at again once it is a quarter of the grace period old, so it registers at
    // least that often: otherwise another node could take documents it still serves for ones nobody registers.
    private TimeSpan Interval =>
        options.MachineDocumentCollectionInterval < options.MachineDocumentGracePeriod / 4
            ? options.MachineDocumentCollectionInterval
            : options.MachineDocumentGracePeriod / 4;

    private async Task CollectAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
                await RegisterAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Could not register the generated documents again; other versions' documents are collected at the "
                        + "next attempt, in {Interval}.",
                    Interval
                );
            }
        }
    }
}
