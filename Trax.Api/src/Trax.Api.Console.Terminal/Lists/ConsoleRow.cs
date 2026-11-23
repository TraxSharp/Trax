using System.Text.Json.Nodes;
using Trax.Api.Console.Terminal.Operations;
using Trax.Client;

namespace Trax.Api.Console.Terminal.Lists;

/// <summary>A row as the terminal and the headless mode use it, whatever list it came from.</summary>
/// <param name="Id">The id its actions take.</param>
/// <param name="Cells">Its columns' text, in column order.</param>
/// <param name="Json">The row as the host wrote it.</param>
/// <param name="Run">The run it is about, for the run trace; null when none.</param>
/// <param name="Code">The type a checkout may hold the source of (a train), for jump to code; null when none.</param>
internal sealed record ConsoleRow(
    long Id,
    IReadOnlyList<string> Cells,
    JsonNode? Json,
    long? Run,
    string? Code
);
