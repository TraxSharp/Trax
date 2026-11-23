using System.Globalization;
using System.Text;

namespace Trax.StateMachine.Gherkin.Lexical;

internal readonly record struct Token(TokenKind Kind, string Value)
{
    internal bool Is(string word) =>
        Kind == TokenKind.Word && string.Equals(Value, word, StringComparison.Ordinal);

    public override string ToString() =>
        Kind switch
        {
            TokenKind.Text => $"\"{Value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"",
            TokenKind.Comma => ",",
            TokenKind.Open => "[",
            TokenKind.Close => "]",
            _ => Value,
        };
}
