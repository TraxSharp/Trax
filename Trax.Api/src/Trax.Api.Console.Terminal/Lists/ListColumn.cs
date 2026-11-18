using System.Text.Json.Nodes;
using Trax.Api.Console.Terminal.Operations;
using Trax.Client;

namespace Trax.Api.Console.Terminal.Lists;

/// <summary>A column of a list as the terminal shows it.</summary>
internal sealed record ListColumn<TRow>(string Name, Func<TRow, string> Value);
