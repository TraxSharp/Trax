using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;

namespace Trax.StateMachine.Persistence;

/// <summary>An edge a server-only trigger fires, as the startup checks read it.</summary>
internal sealed record ServerEdge(string From, string Trigger, string To);
