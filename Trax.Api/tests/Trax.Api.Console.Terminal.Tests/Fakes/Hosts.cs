using System.Text.Json.Nodes;
using Trax.Api.Console.Terminal.Connection;
using Trax.Client;

namespace Trax.Api.Console.Terminal.Tests.Fakes;

/// <summary>Sessions and command lines over a scripted host, and the answers a test refuses with.</summary>
internal static class Hosts
{
    public const string Url = "https://ops.example.com";

    /// <summary>A session that sends through <paramref name="host"/>.</summary>
    public static ConsoleSession Session(ITraxTransport host) =>
        ConsoleSession.Over(
            ConsoleTarget.Resolve(
                Url,
                ConsoleTarget.DefaultGraphQLPath,
                apiKeyFile: null,
                tokenFile: null,
                sendText: false,
                _ => null
            ),
            host
        );

    /// <summary>What a host of <paramref name="version"/> answers <c>ServerVersion</c> with.</summary>
    public static string Version(string version) =>
        new JsonObject
        {
            ["operations"] = new JsonObject
            {
                ["config"] = new JsonObject { ["version"] = version },
            },
        }.ToJsonString();

    /// <summary>
    /// Runs <c>trax-console</c> with <paramref name="args"/> (after <c>--host</c>) against <paramref name="host"/>; its
    /// exit code and what it wrote to standard output and standard error.
    /// </summary>
    public static Task<(int Exit, string Output, string Error)> RunAsync(
        ITraxTransport host,
        params string[] args
    ) => RunAsync(host, CancellationToken.None, args);

    /// <inheritdoc cref="RunAsync(ITraxTransport, string[])"/>
    public static async Task<(int Exit, string Output, string Error)> RunAsync(
        ITraxTransport host,
        CancellationToken cancellationToken,
        params string[] args
    )
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = await ConsoleCommandLine
            .Create(_ => null, target => ConsoleSession.Over(target, host), output, error)
            .Parse(["--host", Url, .. args])
            .InvokeAsync(cancellationToken: cancellationToken);
        return (exit, output.ToString(), error.ToString());
    }

    /// <summary>A host of this tool's release whose lists answer as <see cref="HostData.Console"/>'s do.</summary>
    public static FakeHost Same() =>
        HostData.Console().Answer("ServerVersion", Version(ConsoleSession.ToolVersion));
}

/// <summary>A host that refuses the operations named, with the message and code given, and answers the rest as
/// <paramref name="inner"/> does.</summary>
internal sealed class RefusingHost(ITraxTransport inner) : ITraxTransport
{
    private readonly Dictionary<string, TraxGraphQLError> _refusals = new(StringComparer.Ordinal);

    public RefusingHost Refuse(string operation, string message, string? code = null)
    {
        lock (_refusals)
            _refusals[operation] = new TraxGraphQLError(message, code, null, null);
        return this;
    }

    public Task<TraxAnswer> ExecuteAsync(
        TraxRequest request,
        CancellationToken cancellationToken = default
    )
    {
        TraxGraphQLError? refusal;
        lock (_refusals)
            _refusals.TryGetValue(request.Document.Name, out refusal);
        return refusal is null
            ? inner.ExecuteAsync(request, cancellationToken)
            : Task.FromResult(new TraxAnswer { Errors = [refusal] });
    }

    public IDisposable Subscribe(TraxRequest request, ISubscriptionSink sink) =>
        inner.Subscribe(request, sink);
}

/// <summary>
/// A host whose streams the test drives: each subscription is held with its sink, so the test can send it a frame,
/// an error or the end. Queries go to <paramref name="inner"/>.
/// </summary>
internal sealed class StreamingHost(ITraxTransport inner) : ITraxTransport
{
    private readonly List<(TraxRequest Request, ISubscriptionSink Sink)> _streams = [];

    public Task<TraxAnswer> ExecuteAsync(
        TraxRequest request,
        CancellationToken cancellationToken = default
    ) => inner.ExecuteAsync(request, cancellationToken);

    private int _subscribed;

    /// <summary>Whether a stream was followed again after it failed, which the client does after an interruption.</summary>
    public bool Interrupted => Volatile.Read(ref _subscribed) > 1;

    public IDisposable Subscribe(TraxRequest request, ISubscriptionSink sink)
    {
        Interlocked.Increment(ref _subscribed);
        lock (_streams)
            _streams.Add((request, sink));
        sink.Started();
        return new Handle(() =>
        {
            lock (_streams)
                _streams.RemoveAll(s => s.Sink == sink);
        });
    }

    /// <summary>The sink of the stream of <paramref name="operation"/> followed now, once it is.</summary>
    public async Task<ISubscriptionSink> StreamAsync(string operation)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        for (; ; )
        {
            lock (_streams)
                if (
                    _streams.FirstOrDefault(s => s.Request.Document.Name == operation).Sink is
                    { } sink
                )
                    return sink;
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Nothing follows {operation}.");
            // allowed-delay: the polling interval of a wait with a deadline.
            await Task.Delay(10);
        }
    }

    private sealed class Handle(Action end) : IDisposable
    {
        public void Dispose() => end();
    }
}

/// <summary>A host that answers nothing until the request is cancelled.</summary>
internal sealed class SilentHost : ITraxTransport
{
    public async Task<TraxAnswer> ExecuteAsync(
        TraxRequest request,
        CancellationToken cancellationToken = default
    )
    {
        // allowed-delay: answers only by being cancelled.
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return new TraxAnswer();
    }

    public IDisposable Subscribe(TraxRequest request, ISubscriptionSink sink) =>
        throw new NotSupportedException();
}
