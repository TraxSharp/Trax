using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Trax.StateMachine.Features;

namespace Trax.Cli.Machines;

/// <summary>
/// Writes a machines directory's event-storming board for <c>trax machine diagram</c>: <c>json</c> (the board as
/// data), <c>mermaid</c> (a flowchart, each sticky coloured by its flow role) and <c>html</c> (one page that needs no
/// network, the board drawn from the data embedded in it, its text set as text and never parsed as markup).
/// </summary>
internal static class BoardWriter
{
    internal static readonly string[] Formats = ["html", "mermaid", "json"];

    internal static string Write(Board.Drawn board, string format) =>
        format switch
        {
            "json" => Json(board)
                .ToJsonString(
                    new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Encoder = System
                            .Text
                            .Encodings
                            .Web
                            .JavaScriptEncoder
                            .UnsafeRelaxedJsonEscaping,
                    }
                ) + "\n",
            "mermaid" => Mermaid(board),
            "html" => Html(board),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
        };

    internal static JsonObject Json(Board.Drawn board) =>
        new()
        {
            ["stickies"] = new JsonArray(
                board
                    .Stickies.Select(s =>
                        (JsonNode)
                            new JsonObject
                            {
                                ["id"] = s.Id,
                                ["kind"] = s.Kind.ToString().ToLowerInvariant(),
                                ["machine"] = s.Machine,
                                ["label"] = s.Label,
                                ["scenarios"] = new JsonArray(
                                    s.Scenarios.Select(p =>
                                            (JsonNode)
                                                new JsonObject
                                                {
                                                    ["file"] = p.File,
                                                    ["line"] = p.Line,
                                                    ["scenario"] = p.Scenario,
                                                }
                                        )
                                        .ToArray()
                                ),
                            }
                    )
                    .ToArray()
            ),
            ["links"] = new JsonArray(
                board
                    .Links.Select(l =>
                        (JsonNode)new JsonObject { ["from"] = l.From, ["to"] = l.To }
                    )
                    .ToArray()
            ),
        };

    // Event storming's sticky colours.
    private static readonly Dictionary<Board.Kind, string> Colours = new()
    {
        [Board.Kind.Actor] = "#f3e3c3",
        [Board.Kind.Command] = "#a7c7e7",
        [Board.Kind.Event] = "#f7b267",
        [Board.Kind.Policy] = "#c9b3e6",
        [Board.Kind.Aggregate] = "#f9e07f",
        [Board.Kind.View] = "#b5dfb0",
    };

    private static string Mermaid(Board.Drawn board)
    {
        var ids = board
            .Stickies.Select((s, i) => (s.Id, Node: $"n{i}"))
            .ToDictionary(x => x.Id, x => x.Node, StringComparer.Ordinal);
        var text = new StringBuilder("flowchart LR\n");
        foreach (var (kind, colour) in Colours)
            text.Append(
                $"  classDef {kind.ToString().ToLowerInvariant()} fill:{colour},stroke:#555,color:#111\n"
            );
        foreach (var group in board.Stickies.GroupBy(s => s.Machine))
        {
            text.Append($"  subgraph {Escape(group.Key)}\n");
            foreach (var sticky in group)
                text.Append(
                    $"    {ids[sticky.Id]}[\"{Escape(sticky.Label)}\"]:::{sticky.Kind.ToString().ToLowerInvariant()}\n"
                );
            text.Append("  end\n");
        }
        foreach (var link in board.Links)
            text.Append($"  {ids[link.From]} --> {ids[link.To]}\n");
        return text.ToString();
    }

    // Mermaid reads a quoted label up to the next quote; entity codes keep quotes and brackets in it as text.
    private static string Escape(string text) =>
        text.Replace("\"", "#quot;", StringComparison.Ordinal)
            .Replace("<", "#lt;", StringComparison.Ordinal)
            .Replace(">", "#gt;", StringComparison.Ordinal);

    private static string Html(Board.Drawn board)
    {
        // The default encoder writes '<', '>' and '&' as \u escapes, so no label can end the script element it sits in.
        var data = Json(board).ToJsonString();
        var colours = string.Concat(
            Colours.Select(c => $".{c.Key.ToString().ToLowerInvariant()}{{background:{c.Value}}}")
        );
        return $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'">
            <title>Machines board</title>
            <style>
            body{font:14px system-ui,sans-serif;margin:16px;color:#111}
            section{margin-bottom:24px}h2{font-size:16px;margin:0 0 8px}
            .row{display:flex;flex-wrap:wrap;gap:8px}
            .sticky{padding:8px 10px;border-radius:3px;box-shadow:0 1px 2px #0003;max-width:240px;cursor:default}
            .sticky small{display:block;opacity:.7}
            ul{margin:4px 0 0;padding-left:16px}
            {{colours}}
            </style>
            </head>
            <body>
            <h1>Machines board</h1>
            <div id="board"></div>
            <script type="application/json" id="data">{{data}}</script>
            <script>
            const board = JSON.parse(document.getElementById("data").textContent);
            const root = document.getElementById("board");
            const machines = [...new Set(board.stickies.map(s => s.machine))];
            for (const machine of machines) {
              const section = document.createElement("section");
              const title = document.createElement("h2");
              title.textContent = machine;
              section.append(title);
              const row = document.createElement("div");
              row.className = "row";
              for (const sticky of board.stickies.filter(s => s.machine === machine)) {
                const note = document.createElement("div");
                note.className = "sticky " + sticky.kind;
                note.dataset.sticky = sticky.id;
                const kind = document.createElement("small");
                kind.textContent = sticky.kind;
                note.append(kind, document.createTextNode(sticky.label));
                if (sticky.scenarios.length > 0) {
                  const list = document.createElement("ul");
                  for (const s of sticky.scenarios) {
                    const item = document.createElement("li");
                    item.textContent = s.file + ":" + s.line + " " + s.scenario;
                    list.append(item);
                  }
                  note.append(list);
                }
                row.append(note);
              }
              section.append(row);
              root.append(section);
            }
            </script>
            </body>
            </html>

            """;
    }
}
