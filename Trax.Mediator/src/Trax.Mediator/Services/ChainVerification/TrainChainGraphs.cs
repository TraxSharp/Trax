using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Core.Monad;
using Trax.Effect.Extensions;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Mediator.Services.ChainVerification;

/// <inheritdoc />
/// <remarks>
/// <para>A chain that was read is kept for the life of the host. A chain that could not be read is
/// not: the failure is remembered for <see cref="RetryAfter"/>, so a page that polls does not
/// rebuild the train on every request, and then the chain is read again, because what failed may
/// have been passing (a database a constructor reached, a scope that could not be disposed).</para>
/// <para>A train whose constructor needs something only a request supplies (the current user, say)
/// cannot be built from a scope of this host's own. It is then built directly from its class, with
/// what the container can supply and nothing for the rest: reading the chain runs none of the
/// train's work, only its declaration. A constructor that refuses that leaves the train without a
/// graph.</para>
/// </remarks>
internal sealed class TrainChainGraphs(
    ITrainDiscoveryService discoveryService,
    IServiceScopeFactory scopeFactory,
    ILogger<TrainChainGraphs>? logger = null,
    TimeProvider? time = null
) : ITrainChainGraphs
{
    /// <summary>How long a chain that could not be read is answered with null before it is read again.</summary>
    internal static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(30);

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    private readonly ConcurrentDictionary<string, Declared> _chains = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _failedAt = new(
        StringComparer.Ordinal
    );

    public ChainGraph? Find(string train) => Lookup(train)?.Graph;

    public DeclaredTrainChain? FindDeclared(string train) => Lookup(train)?.Chain;

    private Declared? Lookup(string train)
    {
        if (string.IsNullOrWhiteSpace(train))
            return null;

        if (_chains.TryGetValue(train, out var known))
            return known;

        var registration = discoveryService
            .DiscoverTrains()
            .FirstOrDefault(r =>
                string.Equals(r.ServiceType.FullName, train, StringComparison.Ordinal)
            );

        if (registration is null)
            return null;

        if (
            _failedAt.TryGetValue(train, out var failedAt)
            && _time.GetUtcNow() - failedAt < RetryAfter
        )
            return null;

        // Two callers asking at once may both read it; a chain is a declaration, so they read the
        // same thing and the first one kept is the one every later caller gets.
        var read = Read(registration);

        if (read is null)
        {
            _failedAt[train] = _time.GetUtcNow();
            return null;
        }

        _failedAt.TryRemove(train, out _);
        return _chains.GetOrAdd(train, read);
    }

    /// <summary>
    /// Reads the train's chain off the caller's thread, so disposing the scope asynchronously
    /// cannot deadlock a caller with a synchronization context (a Blazor circuit).
    /// </summary>
    private Declared? Read(TrainRegistration registration) =>
        Task.Run(() => ReadAsync(registration)).GetAwaiter().GetResult();

    private async Task<Declared?> ReadAsync(TrainRegistration registration)
    {
        try
        {
            // An async scope, because a train's scoped dependency may implement only
            // IAsyncDisposable, which a synchronous Dispose refuses outright, as the startup check
            // does.
            await using var scope = scopeFactory.CreateAsyncScope();
            var train = Build(scope.ServiceProvider, registration);

            var chain = (ChainRecorder)
                train
                    .GetType()
                    .GetMethod(nameof(Core.Train.Train<,>.DeclaredChain), Type.EmptyTypes)!
                    .Invoke(train, null)!;

            return new Declared(
                ChainGraph.From(
                    chain,
                    train.GetType(),
                    registration.InputType,
                    registration.OutputType
                ),
                new DeclaredTrainChain(
                    train.GetType(),
                    chain,
                    registration.InputType,
                    registration.OutputType
                )
            );
        }
        catch (Exception e)
        {
            logger?.LogWarning(
                e,
                "The chain of train {Train} could not be read outside a request, so it has no "
                    + "graph for now; it is read again after {RetryAfter}.",
                registration.ServiceType.FullName,
                RetryAfter
            );

            return null;
        }
    }

    /// <summary>
    /// The train as the container builds it or, when that fails (a constructor argument only a
    /// request supplies), built from its class with what the container can supply.
    /// </summary>
    private static object Build(IServiceProvider services, TrainRegistration registration)
    {
        try
        {
            return services.GetRequiredService(registration.ServiceType);
        }
        catch (Exception) when (Constructor(registration.ImplementationType) is { } constructor)
        {
            var arguments = constructor
                .GetParameters()
                .Select(p =>
                    Resolve(services, p.ParameterType)
                    ?? (
                        p.HasDefaultValue ? p.DefaultValue
                        : p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType)
                        : null
                    )
                )
                .ToArray();

            var train = constructor.Invoke(arguments);

            try
            {
                services.InjectProperties(train);
            }
            catch (Exception)
            {
                // A property only a request fills stays empty; the declaration does not read it.
            }

            return train;
        }
    }

    /// <summary>The class's one public constructor, or null when it has none or several.</summary>
    private static ConstructorInfo? Constructor(Type type) =>
        type.IsAbstract ? null
        : type.GetConstructors() is [var only] ? only
        : null;

    private static object? Resolve(IServiceProvider services, Type type)
    {
        try
        {
            return services.GetService(type);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>A train's graph and the declaration it was drawn from, read together once.</summary>
    private sealed record Declared(ChainGraph Graph, DeclaredTrainChain Chain);
}
