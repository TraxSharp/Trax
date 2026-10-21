namespace Trax.Samples.Board.Auth;

/// <summary>
/// The demo API keys, one per client on the board. They are published in this repository, so the host registers them
/// only in Development, and a key carrying <c>do-not-use-in-production</c> refuses to start anywhere else.
/// </summary>
public static class BoardKeys
{
    /// <summary>triage-bot, role <c>triage</c>.</summary>
    public const string TriageBot = "board-triage-bot-key-do-not-use-in-production";

    /// <summary>repro-bot, role <c>reproducer</c>.</summary>
    public const string ReproBot = "board-repro-bot-key-do-not-use-in-production";

    /// <summary>fix-bot, role <c>fixer</c>.</summary>
    public const string FixBot = "board-fix-bot-key-do-not-use-in-production";

    /// <summary>qa-bot, role <c>qa</c>.</summary>
    public const string QaBot = "board-qa-bot-key-do-not-use-in-production";

    /// <summary>Sam, a person: roles <c>reviewer</c> and <c>reporter</c>.</summary>
    public const string Sam = "board-sam-key-do-not-use-in-production";

    /// <summary>The operations console: role <c>Operator</c>.</summary>
    public const string Operator = "board-operator-key-do-not-use-in-production";
}
