using Trax.Api.Console.Terminal.Screens;

namespace Trax.Api.Console.Terminal.Tests.Fakes;

/// <summary>Answers the console's questions as the test says, and keeps what it was asked and shown.</summary>
internal sealed class ScriptedPrompts : IPrompts
{
    public Queue<int?> Choices { get; } = new();

    public Queue<IReadOnlyList<string>?> Forms { get; } = new();

    public List<string> Asked { get; } = [];

    public List<(string Title, IReadOnlyList<string> Lines)> Shown { get; } = [];

    public int? Ask(string title, string message, params string[] buttons)
    {
        Asked.Add($"{title}: {message} [{string.Join("|", buttons)}]");
        return Choices.Count > 0 ? Choices.Dequeue() : null;
    }

    public void Tell(string title, string message) => Asked.Add($"{title}: {message}");

    public IReadOnlyList<string>? Form(
        string title,
        IReadOnlyList<(string Label, string Value)> fields,
        string accept = "OK",
        string? clear = null
    )
    {
        Asked.Add($"{title}: {string.Join(", ", fields.Select(f => f.Label))}");
        return Forms.Count > 0 ? Forms.Dequeue() : null;
    }

    public void Show(string title, IEnumerable<string> lines) => Shown.Add((title, lines.ToList()));
}
