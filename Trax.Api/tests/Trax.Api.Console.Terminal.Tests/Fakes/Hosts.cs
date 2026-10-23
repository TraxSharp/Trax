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
    public static async Task<(int Exit, string Output, string Error)> RunAsync(
        ITraxTransport host,
        params string[] args
    )
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = await ConsoleCommandLine
            .Create(_ => null, target => ConsoleSession.Over(target, host), output, error)
            .Parse(["--host", Url, .. args])
            .InvokeAsync();
        return (exit, output.ToString(), error.ToString());
    }
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
