using System.Text.Json.Nodes;
using Trax.Api.Console.Terminal.Operations;
using Trax.Client;

namespace Trax.Api.Console.Terminal.Connection;

/// <summary>
/// Says what to do about a document the host no longer serves. A host answers <c>CLIENT_OUTDATED</c> to the documents
/// of a release it does not run, as after a deploy in the middle of a session; the refusal then names the code, the
/// host's release and the install line, as the check at connect does, wherever the console shows it.
/// </summary>
internal sealed class OutdatedTransport(ITraxTransport inner) : ITraxTransport
{
    public const string Code = "CLIENT_OUTDATED";

    private string? _hostVersion;

    /// <summary>The release the host last said it runs, when it was a release number.</summary>
    public string? HostVersion
    {
        get => Volatile.Read(ref _hostVersion);
        set => Volatile.Write(ref _hostVersion, value);
    }

    public async Task<TraxAnswer> ExecuteAsync(
        TraxRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var answer = await inner.ExecuteAsync(request, cancellationToken);
        if (
            request.Document.Name == TraxOperations.ServerVersion.Name
            || !answer.Errors.Any(e => e.Code == Code)
        )
            return answer;
        // The host may have moved on since it was last asked.
        var version = await new TraxClient(inner).ServerVersionAsync(cancellationToken);
        if (version.Data?.Operations?.Config.Version is { } host && ConsoleSession.IsRelease(host))
            HostVersion = host;
        return answer with
        {
            Errors = answer
                .Errors.Select(e => e.Code == Code ? e with { Message = Explained(e.Message) } : e)
                .ToList(),
        };
    }

    public IDisposable Subscribe(TraxRequest request, ISubscriptionSink sink) =>
        inner.Subscribe(request, new Sink(sink, this));

    /// <summary>A <c>CLIENT_OUTDATED</c> refusal's message, with the code and what to install.</summary>
    public string Explained(string message) =>
        HostVersion is { } host && host != ConsoleSession.ToolVersion
            ? $"{Code}: {message} {new VersionCheck.Other(host).Message}"
            : $"{Code}: {message} The host serves the documents of another Trax release than trax-console "
                + $"{ConsoleSession.ToolVersion}: trax-console check names the one to install.";

    // A stream's refusal arrives as a frame's errors or as the error the socket ends it with.
    private sealed class Sink(ISubscriptionSink inner, OutdatedTransport transport)
        : ISubscriptionSink
    {
        public void Next(JsonObject value)
        {
            if (value["errors"] is JsonArray)
            {
                var copy = (JsonObject)value.DeepClone();
                if (Explain((JsonArray)copy["errors"]!))
                    value = copy;
            }
            inner.Next(value);
        }

        public void Error(Exception error)
        {
            if (error is SubscriptionErrorException refused)
            {
                var errors = (JsonArray)refused.Errors.DeepClone();
                if (Explain(errors))
                    error = new SubscriptionErrorException(errors);
            }
            inner.Error(error);
        }

        public void Complete() => inner.Complete();

        public void Started() => inner.Started();

        private bool Explain(JsonArray errors)
        {
            var changed = false;
            foreach (var error in errors.OfType<JsonObject>())
                if (
                    error["extensions"]?["code"] is JsonValue code
                    && code.TryGetValue<string>(out var text)
                    && text == Code
                )
                {
                    var message =
                        error["message"] is JsonValue m && m.TryGetValue<string>(out var said)
                            ? said
                            : "The host refused the document.";
                    error["message"] = transport.Explained(message);
                    changed = true;
                }
            return changed;
        }
    }
}
