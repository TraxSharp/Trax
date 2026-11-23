using System.Text.Json.Nodes;
using Trax.Api.Console.Terminal.Operations;
using Trax.Client;

namespace Trax.Api.Console.Terminal.Lists;

/// <summary>What an action is run with besides the rows.</summary>
internal sealed record ActionOptions(string? Note = null, bool AskAfresh = false);
