using System.Text.Json.Nodes;
using Trax.StateMachine.Client;

namespace Trax.StateMachine.Tests.Client;

// A server the test answers by hand: each event and read waits until the test settles it. The first read answers what
// Answer was given; every later one is handed to the test through Reads.
internal sealed class HandServer : IMachineServer
{
    public sealed record Sent(string Trigger, JsonNode? Input, string? ClientResult)
    {
        public TaskCompletionSource<ServerAnswer> Answer { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public System.Threading.Channels.Channel<Sent> Events { get; } =
        System.Threading.Channels.Channel.CreateUnbounded<Sent>();

    public System.Threading.Channels.Channel<TaskCompletionSource<ServerAnswer>> Reads { get; } =
        System.Threading.Channels.Channel.CreateUnbounded<TaskCompletionSource<ServerAnswer>>();

    private readonly TaskCompletionSource<ServerAnswer> _read = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private int _reads;
    private Action<StreamMessage>? _onMessage;

    // Opens the controller and answers its first read.
    public async Task Answer(ServerMachineController controller, ServerAnswer read)
    {
        _read.SetResult(read);
        await controller.StartAsync().WaitAsync(TimeSpan.FromSeconds(10));
    }

    public Task<ServerAnswer> StartAsync(
        JsonNode? input,
        string requestId,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public Task<ServerAnswer> EventAsync(
        string id,
        string trigger,
        JsonNode? input,
        string requestId,
        string? clientResult,
        CancellationToken cancellationToken = default
    )
    {
        var sent = new Sent(trigger, input, clientResult);
        Events.Writer.TryWrite(sent);
        return sent.Answer.Task;
    }

    public Task<ServerAnswer> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Increment(ref _reads) == 1)
            return _read.Task;
        var read = new TaskCompletionSource<ServerAnswer>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        Reads.Writer.TryWrite(read);
        return read.Task;
    }

    public IDisposable Subscribe(string id, Action<StreamMessage> onMessage)
    {
        _onMessage = onMessage;
        return new Nothing();
    }

    // Delivers a message on the instance's stream, as a push from the server would arrive.
    public void Push(StreamMessage message) =>
        (_onMessage ?? throw new InvalidOperationException("Nothing subscribed."))(message);

    private sealed class Nothing : IDisposable
    {
        public void Dispose() { }
    }
}
