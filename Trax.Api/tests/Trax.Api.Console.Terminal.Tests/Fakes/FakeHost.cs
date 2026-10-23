using System.Text.Json.Nodes;
using Trax.Client;

namespace Trax.Api.Console.Terminal.Tests.Fakes;

/// <summary>
/// A host the test scripts: each document, by its operation's name, answers what the test says, every request is
/// kept, and each subscription is held so the test can push frames to it.
/// </summary>
internal sealed class FakeHost : ITraxTransport
{
    private readonly Dictionary<string, Func<JsonObject, Task<JsonObject>>> _answers = new(
        StringComparer.Ordinal
    );
    private readonly List<TraxRequest> _requests = [];
    private readonly List<(TraxRequest Request, ISubscriptionSink Sink)> _streams = [];

    /// <summary>Answers <paramref name="operation"/> with <paramref name="data"/>, whatever its variables.</summary>
    public FakeHost Answer(string operation, string data) =>
        Answer(operation, _ => (JsonObject)JsonNode.Parse(data)!);

    /// <summary>Answers <paramref name="operation"/> with the data <paramref name="answer"/> builds from its variables.</summary>
    public FakeHost Answer(string operation, Func<JsonObject, JsonObject> answer) =>
        AnswerLater(operation, v => Task.FromResult(answer(v)));

    /// <summary>
    /// Answers <paramref name="operation"/> when the task <paramref name="answer"/> returns completes, so a test can
    /// hold one answer back and let a later request overtake it.
    /// </summary>
    public FakeHost AnswerLater(string operation, Func<JsonObject, Task<JsonObject>> answer)
    {
        lock (_answers)
            _answers[operation] = answer;
        return this;
    }

    /// <summary>Every request sent, in order.</summary>
    public IReadOnlyList<TraxRequest> Requests
    {
        get
        {
            lock (_requests)
                return _requests.ToList();
        }
    }

    /// <summary>The variables of every request for <paramref name="operation"/>, in order.</summary>
    public IReadOnlyList<JsonObject> Sent(string operation) =>
        Requests.Where(r => r.Document.Name == operation).Select(r => r.Variables).ToList();

    public async Task<TraxAnswer> ExecuteAsync(
        TraxRequest request,
        CancellationToken cancellationToken = default
    )
    {
        lock (_requests)
            _requests.Add(request);
        Func<JsonObject, Task<JsonObject>>? answer;
        lock (_answers)
            _answers.TryGetValue(request.Document.Name, out answer);
        return answer is null
            ? new TraxAnswer
            {
                Errors =
                [
                    new TraxGraphQLError(
                        $"The fake host has no answer for {request.Document.Name}.",
                        "FAKE",
                        null,
                        null
                    ),
                ],
            }
            : new TraxAnswer { Data = await answer(request.Variables) };
    }

    public IDisposable Subscribe(TraxRequest request, ISubscriptionSink sink)
    {
        lock (_streams)
            _streams.Add((request, sink));
        sink.Started();
        return new Ended(() =>
        {
            lock (_streams)
                _streams.RemoveAll(s => s.Sink == sink);
        });
    }

    /// <summary>Whether a stream of <paramref name="operation"/> is followed now.</summary>
    public bool Follows(string operation)
    {
        lock (_streams)
            return _streams.Any(s => s.Request.Document.Name == operation);
    }

    /// <summary>Pushes a frame with <paramref name="data"/> to every follower of <paramref name="operation"/>.</summary>
    public void Push(string operation, string data)
    {
        List<ISubscriptionSink> sinks;
        lock (_streams)
            sinks = _streams
                .Where(s => s.Request.Document.Name == operation)
                .Select(s => s.Sink)
                .ToList();
        foreach (var sink in sinks)
            sink.Next(new JsonObject { ["data"] = JsonNode.Parse(data) });
    }

    private sealed class Ended(Action end) : IDisposable
    {
        public void Dispose() => end();
    }
}
