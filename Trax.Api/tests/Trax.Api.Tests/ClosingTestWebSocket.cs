using System.Net.WebSockets;
using Microsoft.AspNetCore.TestHost;

namespace Trax.Api.Tests;

public static class ClosingTestWebSocketExtensions
{
    private static readonly TimeSpan ConnectDeadline = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Connects through the test server and returns a socket that sends a close frame when it is
    /// disposed. Use it instead of <see cref="WebSocketClient.ConnectAsync"/> in every test.
    ///
    /// <para>Disposing a test-server client socket closes only the client end: the server end
    /// stays <c>Open</c> and every receive on it throws. Hot Chocolate swallows that and reads
    /// again, so each undisposed connection spins a thread for the rest of the test run. On a
    /// two-core CI runner a few dozen of them starve the suite until the job times out. A close
    /// frame moves the server end to <c>CloseReceived</c>, which ends Hot Chocolate's read loop.</para>
    ///
    /// <para>The connect is bounded too, so a stalled upgrade fails the test instead of hanging
    /// the run.</para>
    /// </summary>
    public static async Task<WebSocket> ConnectClosingAsync(
        this WebSocketClient client,
        Uri uri,
        CancellationToken cancellationToken = default
    )
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(ConnectDeadline);
        return new ClosingTestWebSocket(await client.ConnectAsync(uri, deadline.Token));
    }

    private sealed class ClosingTestWebSocket(WebSocket inner) : WebSocket
    {
        public override WebSocketCloseStatus? CloseStatus => inner.CloseStatus;
        public override string? CloseStatusDescription => inner.CloseStatusDescription;
        public override WebSocketState State => inner.State;
        public override string? SubProtocol => inner.SubProtocol;

        public override void Abort() => inner.Abort();

        public override Task CloseAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken
        ) => inner.CloseAsync(closeStatus, statusDescription, cancellationToken);

        public override Task CloseOutputAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken
        ) => inner.CloseOutputAsync(closeStatus, statusDescription, cancellationToken);

        public override Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken
        ) => inner.ReceiveAsync(buffer, cancellationToken);

        public override Task SendAsync(
            ArraySegment<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken
        ) => inner.SendAsync(buffer, messageType, endOfMessage, cancellationToken);

        public override void Dispose()
        {
            if (inner.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    inner
                        .CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, cts.Token)
                        .GetAwaiter()
                        .GetResult();
                }
                catch (Exception ex)
                    when (ex
                            is WebSocketException
                                or InvalidOperationException
                                or ObjectDisposedException
                                or OperationCanceledException
                    )
                {
                    // The server already closed or went away: nothing is left to stop.
                }
            }

            inner.Dispose();
        }
    }
}
