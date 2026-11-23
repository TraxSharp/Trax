using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Trax.StateMachine.Tests.Client;

/// <summary>
/// A graphql-transport-ws host on a loopback port, with no web host: each connection it accepts is handed to the next of
/// its scripts, the last one repeating.
/// </summary>
internal sealed class WireHost : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly Func<WirePeer, Task>[] _scripts;
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Task> _peers = [];
    private readonly Task _accepting;
    private int _connections;

    public WireHost(params Func<WirePeer, Task>[] scripts)
    {
        _scripts = scripts;
        _listener.Start();
        _accepting = AcceptAsync();
    }

    public Uri Endpoint =>
        new(
            string.Create(
                CultureInfo.InvariantCulture,
                $"ws://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/graphql"
            )
        );

    /// <summary>How many connections were accepted.</summary>
    public int Connections => Volatile.Read(ref _connections);

    private async Task AcceptAsync()
    {
        try
        {
            for (; ; )
            {
                var tcp = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
                var n = Interlocked.Increment(ref _connections);
                var script = _scripts[Math.Min(n - 1, _scripts.Length - 1)];
                lock (_peers)
                    _peers.Add(RunAsync(tcp, script));
            }
        }
        catch (Exception ex)
            when (_stop.IsCancellationRequested
                && ex
                    is OperationCanceledException
                        or InvalidOperationException
                        or ObjectDisposedException
                        or SocketException
            )
        {
            // Disposed. A loop that comes back round after Stop() is told the listener is not listening.
        }
    }

    private async Task RunAsync(TcpClient tcp, Func<WirePeer, Task> script)
    {
        using (tcp)
        using (var peer = new WirePeer(tcp.GetStream(), _stop.Token))
        {
            try
            {
                await script(peer).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The client went away, or the host is stopping.
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        _listener.Stop();
        await _accepting.ConfigureAwait(false);
        Task[] peers;
        lock (_peers)
            peers = [.. _peers];
        await Task.WhenAll(peers)
            .WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None)
            .ConfigureAwait(false);
        _stop.Dispose();
    }
}
