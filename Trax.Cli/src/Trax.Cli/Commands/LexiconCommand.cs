using System.CommandLine;
using System.Globalization;
using System.Text;
using Trax.StateMachine.Gherkin;
using Trax.StateMachine.Gherkin.Lexical;

namespace Trax.Cli.Commands;

/// <summary>
/// <c>trax lexicon</c>: tools for the lexicon, the file people write to say what their features' words mean
/// (<c>Trax.StateMachine/conformance/lexicon.md</c>). The reader ignores layout, so nothing here changes what a lexicon
/// elaborates to.
/// </summary>
internal static class LexiconCommand
{
    public static Command Create() =>
        new("lexicon", "Work with lexicons, the files that say what features' words mean.")
        {
            CreateFormat(),
        };

    private static Command CreateFormat()
    {
        var pathsArgument = new Argument<string[]>("paths")
        {
            Description =
                "Lexicon files, or directories searched for *.lexicon (default: the current directory).",
            Arity = ArgumentArity.ZeroOrMore,
        };
        var checkOption = new Option<bool>("--check")
        {
            Description = "Write nothing; exit 1 if a lexicon is not in its layout.",
        };
        var command = new Command(
            "format",
            "Lay a lexicon out in one way: two spaces per block, its columns lined up. What it elaborates to is unchanged."
        )
        {
            pathsArgument,
            checkOption,
        };
        command.SetAction(
            (parseResult, _) =>
                RunFormatAsync(
                    parseResult.GetValue(pathsArgument) is { Length: > 0 } paths
                        ? paths
                        : [Directory.GetCurrentDirectory()],
                    parseResult.GetValue(checkOption),
                    parseResult.InvocationConfiguration.Output,
                    parseResult.InvocationConfiguration.Error
                )
        );
        return command;
    }

    // Build output and dependencies hold copies of lexicons, never their sources.
    private static readonly HashSet<string> Skipped = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin",
        "obj",
        "node_modules",
    };

    private static IEnumerable<string> Lexicons(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*" + Lexicon.Extension))
            yield return file;
        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            var name = Path.GetFileName(child);
            if (Skipped.Contains(name) || name.StartsWith('.'))
                continue;
            foreach (var file in Lexicons(child))
                yield return file;
        }
    }

    internal static async Task<int> RunFormatAsync(
        IReadOnlyList<string> paths,
        bool check,
        TextWriter stdout,
        TextWriter stderr
    )
    {
        var files = new List<string>();
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
                files.AddRange(Lexicons(path).Order(StringComparer.Ordinal));
            else if (File.Exists(path))
                files.Add(path);
            else
            {
                await stderr.WriteLineAsync($"{path} does not exist.").ConfigureAwait(false);
                return 1;
            }
        }
        if (files.Count == 0)
        {
            await stderr
                .WriteLineAsync($"No lexicon (*{Lexicon.Extension}) in {string.Join(", ", paths)}.")
                .ConfigureAwait(false);
            return 1;
        }

        var failed = false;
        foreach (var file in files)
        {
            string text;
            try
            {
                text = await File.ReadAllTextAsync(file).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await stderr.WriteLineAsync(ex.Message).ConfigureAwait(false);
                failed = true;
                continue;
            }
            var (formatted, problems) = LexiconFormatter.Format(text);
            if (formatted is null)
            {
                foreach (var problem in problems)
                    await stderr
                        .WriteLineAsync(
                            string.Create(
                                CultureInfo.InvariantCulture,
                                $"{file}:{problem.Line}: {problem.Message}"
                            )
                        )
                        .ConfigureAwait(false);
                failed = true;
                continue;
            }
            if (
                string.Equals(
                    formatted,
                    text.Replace("\r\n", "\n", StringComparison.Ordinal),
                    StringComparison.Ordinal
                )
            )
                continue;
            if (check)
            {
                await stderr.WriteLineAsync($"{file} is not in its layout.").ConfigureAwait(false);
                failed = true;
                continue;
            }
            try
            {
                await File.WriteAllTextAsync(
                        file,
                        formatted,
                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
                    )
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await stderr.WriteLineAsync(ex.Message).ConfigureAwait(false);
                failed = true;
                continue;
            }
            await stdout.WriteLineAsync($"formatted {file}").ConfigureAwait(false);
        }
        if (failed && check)
            await stderr
                .WriteLineAsync(
                    "Run `trax lexicon format` without --check and commit what it writes."
                )
                .ConfigureAwait(false);
        return failed ? 1 : 0;
    }
}
