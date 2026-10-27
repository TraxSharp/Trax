using AwesomeAssertions;
using Trax.Api.Console.Terminal.Code;
using Trax.Api.Console.Terminal.Connection;

namespace Trax.Api.Console.Terminal.Tests.UnitTests;

/// <summary>
/// Jump to code finds a type the host names in the checkout by its name alone, a train by its class before its
/// interface, and opens it at its line in the editors that take one.
/// </summary>
[TestFixture]
public class CodeFinderTests
{
    private DirectoryInfo _root = null!;

    [SetUp]
    public void Checkout()
    {
        _root = Directory.CreateTempSubdirectory("trax-console-code-");
        Write(
            "src/Shop/Trains/IShipOrderTrain.cs",
            "namespace Shop.Trains;\npublic interface IShipOrderTrain { }\n"
        );
        Write(
            "src/Shop/Trains/ShipOrderTrain.cs",
            "namespace Shop.Trains;\n\n// The train.\ninternal sealed class ShipOrderTrain : IShipOrderTrain { }\n"
        );
        Write("src/Shop/Junctions/ChargeCard.cs", "namespace Shop;\npublic class ChargeCard { }\n");
        Write("src/Shop/bin/Debug/Copy.cs", "public class ChargeCard { }\n");
    }

    [TearDown]
    public void Remove() => _root.Delete(recursive: true);

    [Test]
    public async Task A_train_named_by_its_interface_opens_at_its_class()
    {
        var found = await CodeFinder.FindAsync(_root.FullName, "Shop.Trains.IShipOrderTrain");

        found.Should().NotBeNull();
        Path.GetFileName(found!.Path).Should().Be("ShipOrderTrain.cs");
        found.Line.Should().Be(4);
    }

    [Test]
    public async Task A_junction_is_found_by_its_name_and_build_output_is_not_searched()
    {
        var found = await CodeFinder.FindAsync(_root.FullName, "ChargeCard");

        found!.Path.Should().EndWith(Path.Combine("Junctions", "ChargeCard.cs"));
    }

    [TestCase("Missing")]
    [TestCase("not a name; rm -rf")]
    public async Task A_name_with_no_declaration_finds_nothing(string name) =>
        (await CodeFinder.FindAsync(_root.FullName, name)).Should().BeNull();

    [Test]
    public async Task A_link_to_a_file_or_a_directory_is_not_followed()
    {
        var outside = Directory.CreateTempSubdirectory("trax-console-outside-");
        try
        {
            File.WriteAllText(Path.Combine(outside.FullName, "Secret.cs"), "class Leaked { }\n");
            File.CreateSymbolicLink(
                Path.Combine(_root.FullName, "src", "Linked.cs"),
                Path.Combine(outside.FullName, "Secret.cs")
            );
            Directory.CreateSymbolicLink(
                Path.Combine(_root.FullName, "src", "away"),
                outside.FullName
            );

            (await CodeFinder.FindAsync(_root.FullName, "Leaked")).Should().BeNull();
        }
        finally
        {
            outside.Delete(recursive: true);
        }
    }

    [Test]
    public async Task A_search_that_is_cancelled_stops() =>
        await FluentActions
            .Awaiting(() =>
                CodeFinder.FindAsync(_root.FullName, "Missing", new CancellationToken(true))
            )
            .Should()
            .ThrowAsync<OperationCanceledException>();

    [Test]
    public void The_search_starts_at_the_checkout_the_console_runs_in()
    {
        Directory.CreateDirectory(Path.Combine(_root.FullName, ".git"));

        CodeFinder.Root(Path.Combine(_root.FullName, "src", "Shop")).Should().Be(_root.FullName);
    }

    [TestCase("nvim", new[] { "+4", "/x/A.cs" })]
    [TestCase("code --wait", new[] { "--wait", "--goto", "/x/A.cs:4" })]
    [TestCase("gedit", new[] { "/x/A.cs" })]
    [TestCase("  nvim   -u NONE ", new[] { "-u", "NONE", "+4", "/x/A.cs" })]
    [TestCase("'/opt/My Editor/bin/vim' --clean", new[] { "--clean", "+4", "/x/A.cs" })]
    [TestCase(
        "emacs -nw --eval \"(setq a \\\"b\\\")\"",
        new[] { "-nw", "--eval", "(setq a \"b\")", "+4", "/x/A.cs" }
    )]
    public void The_editor_gets_the_line_when_it_takes_one(string editor, string[] arguments) =>
        CodeFinder
            .Open(editor, new SourceLocation("/x/A.cs", 4), UnixPath, AnyFile)
            .ArgumentList.Should()
            .Equal(arguments);

    [Test]
    public void The_editor_does_not_inherit_the_credential()
    {
        var saved = (
            Key: Environment.GetEnvironmentVariable(ConsoleTarget.ApiKeyVariable),
            Token: Environment.GetEnvironmentVariable(ConsoleTarget.TokenVariable)
        );
        try
        {
            Environment.SetEnvironmentVariable(ConsoleTarget.ApiKeyVariable, "sk_live_1");
            Environment.SetEnvironmentVariable(ConsoleTarget.TokenVariable, "eyJ.token");

            var info = CodeFinder.Open("nvim", new SourceLocation("/x/A.cs", 4), UnixPath, AnyFile);

            info.Environment.Should().NotContainKey(ConsoleTarget.ApiKeyVariable);
            info.Environment.Should().NotContainKey(ConsoleTarget.TokenVariable);
            info.Environment.Should().ContainKey("PATH");
        }
        finally
        {
            Environment.SetEnvironmentVariable(ConsoleTarget.ApiKeyVariable, saved.Key);
            Environment.SetEnvironmentVariable(ConsoleTarget.TokenVariable, saved.Token);
        }
    }

    [Test]
    public void A_quoted_editor_path_is_one_word() =>
        CodeFinder
            .Open("'/opt/My Editor/bin/vim' --clean", new SourceLocation("/x/A.cs", 4))
            .FileName.Should()
            .Be("/opt/My Editor/bin/vim");

    [Test]
    public void The_editor_is_VISUAL_then_EDITOR_then_vi()
    {
        CodeFinder.Editor(n => n == "VISUAL" ? "code" : "nano").Should().Be("code");
        CodeFinder.Editor(n => n == "EDITOR" ? "nano" : null).Should().Be("nano");
        CodeFinder.Editor(_ => null).Should().Be("vi");
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("\t\n")]
    public void A_blank_editor_is_not_set(string blank)
    {
        CodeFinder.Editor(n => n == "VISUAL" ? blank : "nano").Should().Be("nano");
        CodeFinder.Editor(_ => blank).Should().Be("vi");
    }

    [Test]
    public void A_bare_name_is_found_in_an_absolute_PATH_entry_only()
    {
        var tried = new List<string>();
        var env = new Dictionary<string, string> { ["PATH"] = ":.:bin:/opt/a:/opt/b/" };

        CodeFinder
            .Command(
                "vim",
                n => env.GetValueOrDefault(n),
                windows: false,
                runnable: p =>
                {
                    tried.Add(p);
                    return p == "/opt/b/vim";
                }
            )
            .Should()
            .Be("/opt/b/vim");
        tried.Should().Equal("/opt/a/vim", "/opt/b/vim");
    }

    [Test]
    public void An_editor_planted_in_the_current_directory_is_not_run()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Unix file modes.");
            return;
        }
        var planted = Path.Combine(_root.FullName, "vim");
        File.WriteAllText(planted, "#!/bin/sh\n");
        File.SetUnixFileMode(planted, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        var previous = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(_root.FullName);

            FluentActions
                .Invoking(() =>
                    CodeFinder.Command("vim", n => n == "PATH" ? ":." : null, windows: false)
                )
                .Should()
                .Throw<InvalidOperationException>()
                .WithMessage("*No 'vim' on PATH*");
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
        }
    }

    [Test]
    public void A_file_nobody_may_execute_is_not_the_editor()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Unix file modes.");
            return;
        }
        var bin = Directory.CreateDirectory(Path.Combine(_root.FullName, "bin")).FullName;
        var file = Path.Combine(bin, "vim");
        File.WriteAllText(file, "");
        File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        FluentActions
            .Invoking(() =>
                CodeFinder.Command("vim", n => n == "PATH" ? bin : null, windows: false)
            )
            .Should()
            .Throw<InvalidOperationException>();

        File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        CodeFinder.Command("vim", n => n == "PATH" ? bin : null, windows: false).Should().Be(file);
    }

    [TestCase("./vim")]
    [TestCase("bin/vim")]
    [TestCase("../tools/code")]
    public void A_relative_path_is_refused(string editor) =>
        FluentActions
            .Invoking(() =>
                CodeFinder.Open(editor, new SourceLocation("/x/A.cs", 4), UnixPath, AnyFile)
            )
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*relative path*");

    [Test]
    public void An_absolute_path_is_used_as_given() =>
        CodeFinder
            .Command("/opt/vim/bin/vim", _ => null, windows: false, runnable: _ => false)
            .Should()
            .Be("/opt/vim/bin/vim");

    [Test]
    public void An_editor_that_is_not_on_PATH_is_refused() =>
        FluentActions
            .Invoking(() =>
                CodeFinder.Command("nvim", UnixPath, windows: false, runnable: _ => false)
            )
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*No 'nvim' on PATH*");

    [Test]
    public void On_Windows_a_bare_name_is_found_on_the_path_with_its_extension()
    {
        var env = new Dictionary<string, string>
        {
            ["PATH"] =
                @"bin;C:\Windows;""C:\Users\op\AppData\Local\Programs\Microsoft VS Code\bin""",
            ["PATHEXT"] = ".COM;.EXE;.BAT;.CMD",
        };
        var there = @"C:\Users\op\AppData\Local\Programs\Microsoft VS Code\bin\code.cmd";
        var tried = new List<string>();

        CodeFinder
            .Command(
                "code",
                n => env.GetValueOrDefault(n),
                windows: true,
                runnable: p =>
                {
                    tried.Add(p);
                    return p == there;
                }
            )
            .Should()
            .Be(there);
        tried.Should().NotContain(p => p.StartsWith("bin"), "a relative entry is never searched");
        FluentActions
            .Invoking(() =>
                CodeFinder.Command(
                    "code",
                    n => env.GetValueOrDefault(n),
                    windows: true,
                    runnable: _ => false
                )
            )
            .Should()
            .Throw<InvalidOperationException>();
        CodeFinder
            .Command(
                @"C:\vim\vim.exe",
                n => env.GetValueOrDefault(n),
                windows: true,
                runnable: _ => false
            )
            .Should()
            .Be(@"C:\vim\vim.exe");
        FluentActions
            .Invoking(() =>
                CodeFinder.Command(@"tools\vim.exe", n => env.GetValueOrDefault(n), windows: true)
            )
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*relative path*");
    }

    private static string? UnixPath(string name) => name == "PATH" ? "/usr/bin" : null;

    private static bool AnyFile(string path) => true;

    private void Write(string path, string text)
    {
        var full = Path.Combine(_root.FullName, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }
}
