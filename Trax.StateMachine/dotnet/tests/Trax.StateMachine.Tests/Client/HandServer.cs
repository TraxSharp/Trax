using System.Text.Json.Nodes;
using Trax.StateMachine.Client;

namespace Trax.StateMachine.Tests.Client;

// A server the test answers by hand: each event and read waits until the test settles it.
internal sealed class HandServer : IMachineServer
{
    public sealed record Sent(string Trigger, JsonNode? Input, string? ClientResult)
    {
        public TaskCompletionSource<ServerAnswer> Answer { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public System.Threading.Channels.Channel<Sent> Events { get; } =
        System.Threading.Channels.Channel.CreateUnbounded<Sent>();

    private readonly TaskCompletionSource<ServerAnswer> _read = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );

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

    public Task<ServerAnswer> GetAsync(string id, CancellationToken cancellationToken = default) =>
        _read.Task;

    public IDisposable Subscribe(string id, Action<StreamMessage> onMessage) => new Nothing();

    private sealed class Nothing : IDisposable
    {
        public void Dispose() { }
    }
}
