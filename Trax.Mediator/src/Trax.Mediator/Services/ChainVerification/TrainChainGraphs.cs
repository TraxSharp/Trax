using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Core.Extensions;
using Trax.Core.Monad;
using Trax.Effect.Extensions;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Mediator.Services.ChainVerification;

/// <summary>
/// The <see cref="ITrainChainGraphs"/> the mediator registers: reads each registered train's
/// declared chain on first request and keeps it.
/// </summary>
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

    // Find and FindDeclared answer synchronously. A chain already read, or one with nothing to
    // read, is answered on the caller's thread; a read runs off it, so disposing the scope
    // asynchronously cannot deadlock a caller with a synchronization context (a Blazor circuit).
    public ChainGraph? Find(string train) =>
        TryKnown(train, out var known, out var toRead)
            ? known?.Graph
            : Task.Run(() => ReadAndKeepAsync(train, toRead)).GetAwaiter().GetResult()?.Graph;

    public DeclaredTrainChain? FindDeclared(string train) =>
        TryKnown(train, out var known, out var toRead)
            ? known?.Chain
            : Task.Run(() => ReadAndKeepAsync(train, toRead)).GetAwaiter().GetResult()?.Chain;

    /// <summary>
    /// True with the answer when <paramref name="train"/> needs no read: it was read already, no
    /// registered train has that name, or its last read failed less than
    /// <see cref="RetryAfter"/> ago. False with the registration to read otherwise.
    /// </summary>
    private bool TryKnown(
        string train,
        out Declared? known,
        [NotNullWhen(false)] out TrainRegistration? toRead
    )
    {
        known = null;
        toRead = null;

        if (string.IsNullOrWhiteSpace(train))
            return true;

        if (_chains.TryGetValue(train, out known))
            return true;

        var registration = discoveryService
            .DiscoverTrains()
            .FirstOrDefault(r =>
                string.Equals(r.ServiceType.FullName, train, StringComparison.Ordinal)
            );

        if (registration is null)
            return true;

        if (
            _failedAt.TryGetValue(train, out var failedAt)
            && _time.GetUtcNow() - failedAt < RetryAfter
        )
            return true;

        toRead = registration;
        return false;
    }

    private async Task<Declared?> ReadAndKeepAsync(string train, TrainRegistration registration)
    {
        // Two callers asking at once may both read it; a chain is a declaration, so they read the
        // same thing and the first one kept is the one every later caller gets.
        var read = await ReadAsync(registration).ConfigureAwait(false);

        if (read is null)
        {
            _failedAt[train] = _time.GetUtcNow();
            return null;
        }

        _failedAt.TryRemove(train, out _);
        return _chains.GetOrAdd(train, read);
    }

    private async Task<Declared?> ReadAsync(TrainRegistration registration)
    {
        try
        {
            // An async scope, because a train's scoped dependency may implement only
            // IAsyncDisposable, which a synchronous Dispose refuses outright, as the startup check
            // does.
            var scope = scopeFactory.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var train = Build(scope.ServiceProvider, registration);

                var chain = (ChainRecorder)
                    train
                        .GetType()
                        .GetMethod(nameof(Core.Train.Train<,>.DeclaredChain), Type.EmptyTypes)
                        .NotNull()
                        .Invoke(train, parameters: null)
                        .NotNull();

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
