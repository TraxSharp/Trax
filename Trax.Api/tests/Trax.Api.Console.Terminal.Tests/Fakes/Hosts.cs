using System.Text.Json.Nodes;
using Trax.Api.Console.Terminal.Connection;
using Trax.Client;

namespace Trax.Api.Console.Terminal.Tests.Fakes;

/// <summary>Sessions and command lines over a scripted host, and the answers a test refuses with.</summary>
internal static class Hosts
{
    public const string Url = "https://ops.example.com";

    /// <summary>A session that sends through <paramref name="host"/>.</summary>
    public static async Task<ConsoleSession> SessionAsync(ITraxTransport host) =>
        ConsoleSession.Over(
            await ConsoleTarget
                .ResolveAsync(
                    Url,
                    ConsoleTarget.DefaultGraphQLPath,
                    apiKeyFile: null,
                    tokenFile: null,
                    sendText: false,
                    _ => null,
                    CancellationToken.None
                )
                .ConfigureAwait(false),
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
            .Create(
                _ => null,
                target => ConsoleSession.Over(target, host),
                output,
                error,
                TimeProvider.System
            )
            .Parse(["--host", Url, .. args])
            .InvokeAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return (exit, output.ToString(), error.ToString());
    }

    /// <summary>A host of this tool's release whose lists answer as <see cref="HostData.Console"/>'s do.</summary>
    public static FakeHost Same() =>
        HostData.Console().Answer("ServerVersion", Version(ConsoleSession.ToolVersion));
}
