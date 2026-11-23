using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;

namespace Trax.Samples.ChatService.E2E.Utilities;

/// <summary>The server closed the socket; carries the close code and reason it sent.</summary>
public sealed class WebSocketClosedException(WebSocketCloseStatus? status, string? description)
    : InvalidOperationException(
        string.Create(
            CultureInfo.InvariantCulture,
            $"WebSocket closed by server: {(int?)status} {description}"
        )
    )
{
    public WebSocketCloseStatus? Status { get; } = status;

    public string? Description { get; } = description;
}
