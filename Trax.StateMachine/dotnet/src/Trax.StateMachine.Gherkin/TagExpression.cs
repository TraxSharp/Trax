using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>
/// Cucumber's tag expressions: tags, <c>not</c>, <c>and</c>, <c>or</c> and parentheses, <c>not</c> binding tightest and
/// <c>or</c> loosest. <c>\</c> escapes a space or parenthesis in a tag.
/// </summary>
internal static class TagExpression
{
    /// <exception cref="FormatException">The expression does not parse; the message says where.</exception>
    internal static Func<IReadOnlyList<string>, bool> Parse(string expression)
    {
        var tokens = Tokens(expression);
        var reader = new Reader(expression, tokens);
        var parsed = reader.Or();
        if (reader.At < tokens.Count)
            throw new FormatException(
                $"'{expression}': '{tokens[reader.At]}' is not expected here."
            );
        return parsed;
    }

    // The recursive descent over the tokens, from where it has read to.
    private sealed class Reader(string expression, List<string> tokens)
    {
        private int _at;

        internal int At => _at;

        internal Func<IReadOnlyList<string>, bool> Or()
        {
            var left = And();
            while (_at < tokens.Count && string.Equals(tokens[_at], "or", StringComparison.Ordinal))
            {
                _at++;
                var l = left;
                var r = And();
                left = t => l(t) || r(t);
            }
            return left;
        }

        private Func<IReadOnlyList<string>, bool> And()
        {
            var left = Not();
            while (
                _at < tokens.Count && string.Equals(tokens[_at], "and", StringComparison.Ordinal)
            )
            {
                _at++;
                var l = left;
                var r = Not();
                left = t => l(t) && r(t);
            }
            return left;
        }

        private Func<IReadOnlyList<string>, bool> Not()
        {
            if (_at < tokens.Count && string.Equals(tokens[_at], "not", StringComparison.Ordinal))
            {
                _at++;
                var inner = Not();
                return t => !inner(t);
            }
            return Atom();
        }

        private Func<IReadOnlyList<string>, bool> Atom()
        {
            if (_at >= tokens.Count)
                throw new FormatException($"'{expression}' ends where a tag is expected.");
            var token = tokens[_at++];
            if (string.Equals(token, "(", StringComparison.Ordinal))
            {
                var inner = Or();
                if (
                    _at >= tokens.Count
                    || !string.Equals(tokens[_at], ")", StringComparison.Ordinal)
                )
                    throw new FormatException($"'{expression}' has a '(' that is not closed.");
                _at++;
                return inner;
            }
            if (token is ")" or "and" or "or")
                throw new FormatException($"'{expression}': '{token}' is not expected here.");
            if (!token.StartsWith('@'))
                throw new FormatException(
                    $"'{expression}': '{token}' is not a tag; a tag starts with @."
                );
            return t => t.Contains(token, StringComparer.Ordinal);
        }
    }

    private static List<string> Tokens(string expression)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        void Flush()
        {
            if (current.Length > 0)
                tokens.Add(current.ToString());
            current.Clear();
        }
        for (var i = 0; i < expression.Length; i++)
        {
            var c = expression[i];
            if (c == '\\' && i + 1 < expression.Length)
                current.Append(expression[++i]);
            else if (char.IsWhiteSpace(c))
                Flush();
            else if (c is '(' or ')')
            {
                Flush();
                tokens.Add(c.ToString());
            }
            else
                current.Append(c);
        }
        Flush();
        return tokens;
    }
}
