using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;

namespace Trax.Samples.Auth.E2E.Utilities;

public sealed class ConnectionRejectedException(string message) : Exception(message);
