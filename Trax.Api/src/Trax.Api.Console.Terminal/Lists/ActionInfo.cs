using System.Text.Json.Nodes;
using Trax.Api.Console.Terminal.Operations;
using Trax.Client;

namespace Trax.Api.Console.Terminal.Lists;

/// <summary>An action without the code that runs it: its name, key and what it asks for.</summary>
internal sealed record ActionInfo(
    string Name,
    char Key,
    string Description,
    bool NeedsNote,
    bool Confirm,
    bool AsksAfresh
);
