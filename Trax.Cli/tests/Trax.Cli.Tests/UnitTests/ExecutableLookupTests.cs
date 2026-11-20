using AwesomeAssertions;
using Trax.Cli.Machines;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// <c>trax machine generate</c> runs from inside a checkout it does not trust, so the <c>node</c> it starts is
/// found on <c>PATH</c> only: a <c>node</c> file planted at the checkout's root must never run, which a bare
/// name handed to <see cref="System.Diagnostics.Process"/> would allow on Unix.
///
/// <para>Enforces <c>docs/adr/0001-the-machine-toolchain-is-half-in-process.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0001-the-machine-toolchain-is-half-in-process.md")]
public class ExecutableLookupTests
{
    private string _root = "";
    private string _previousDirectory = "";

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"trax-lookup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _previousDirectory = Directory.GetCurrentDirectory();
    }

    [TearDown]
    public void TearDown()
    {
        Directory.SetCurrentDirectory(_previousDirectory);
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Test]
    public async Task A_node_planted_in_the_current_directory_never_runs()
    {
        IgnoreOnWindows();
        var checkout = Directory.CreateDirectory(Path.Combine(_root, "checkout")).FullName;
        var marker = Path.Combine(_root, "planted-node-ran");
        await Script(Path.Combine(checkout, "node"), $"touch '{marker}'").ConfigureAwait(false);
        Directory.SetCurrentDirectory(checkout);

        // The empty entry and '.' are how a PATH says "the current directory".
        var runner = new NodeRunner(
            "node",
            name => ExecutableLookup.Resolve(name, ":.:checkout", pathExt: null, windows: false)
        );

        (await runner.IsAvailableAsync().ConfigureAwait(false))
            .Should()
            .BeFalse("no absolute PATH entry holds a node");
        File.Exists(marker)
            .Should()
            .BeFalse(
                "the node in the current directory must not be run. See "
                    + "docs/adr/0001-the-machine-toolchain-is-half-in-process.md."
            );
    }

    [Test]
    public async Task The_node_on_PATH_runs_instead_of_one_in_the_current_directory()
    {
        IgnoreOnWindows();
        var checkout = Directory.CreateDirectory(Path.Combine(_root, "checkout")).FullName;
        var bin = Directory.CreateDirectory(Path.Combine(_root, "bin")).FullName;
        var marker = Path.Combine(_root, "planted-node-ran");
        await Script(Path.Combine(checkout, "node"), $"touch '{marker}'").ConfigureAwait(false);
        await Script(Path.Combine(bin, "node"), "echo v24.0.0").ConfigureAwait(false);
        Directory.SetCurrentDirectory(checkout);

        var runner = new NodeRunner(
            "node",
            name => ExecutableLookup.Resolve(name, $".:{bin}", pathExt: null, windows: false)
        );

        (await runner.IsAvailableAsync().ConfigureAwait(false)).Should().BeTrue();
        (await runner.RunAsync("--version", []).ConfigureAwait(false))
            .StdOut.Trim()
            .Should()
            .Be("v24.0.0");
        File.Exists(marker).Should().BeFalse();
    }

    [Test]
    public async Task The_real_lookup_does_not_resolve_to_the_current_directory()
    {
        IgnoreOnWindows();
        var checkout = Directory.CreateDirectory(Path.Combine(_root, "checkout")).FullName;
        var planted = Path.Combine(checkout, "node");
        await Script(planted, "exit 0").ConfigureAwait(false);
        Directory.SetCurrentDirectory(checkout);

        ExecutableLookup.Resolve("node").Should().NotBe(planted).And.NotBe("node");
    }

    [Test]
    public async Task A_file_without_an_execute_bit_is_skipped()
    {
        IgnoreOnWindows();
        var first = Directory.CreateDirectory(Path.Combine(_root, "first")).FullName;
        var second = Directory.CreateDirectory(Path.Combine(_root, "second")).FullName;
        await File.WriteAllTextAsync(Path.Combine(first, "node"), "not executable");
        await Script(Path.Combine(second, "node"), "exit 0").ConfigureAwait(false);

        ExecutableLookup
            .Resolve("node", $"{first}:{second}", pathExt: null, windows: false)
            .Should()
            .Be(Path.Combine(second, "node"));
    }

    [Test]
    public async Task An_absolute_path_is_used_as_given_and_a_missing_one_is_not_found()
    {
        IgnoreOnWindows();
        var node = Path.Combine(_root, "node");
        await Script(node, "exit 0").ConfigureAwait(false);

        ExecutableLookup.Resolve(node, "", pathExt: null, windows: false).Should().Be(node);
        ExecutableLookup
            .Resolve(Path.Combine(_root, "absent"), "", pathExt: null, windows: false)
            .Should()
            .BeNull();
    }

    [TestCase("./node", false)]
    [TestCase("bin/node", false)]
    [TestCase(@".\node.exe", true)]
    [TestCase(@"tools\node", true)]
    [TestCase("C:node.exe", true)]
    public void A_relative_path_is_refused(string command, bool windows)
    {
        var resolve = () => ExecutableLookup.Resolve(command, "/usr/bin", pathExt: null, windows);

        resolve
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*--node must be an absolute path or a command name found on PATH*");
    }

    [Test]
    public void A_name_not_on_PATH_is_not_found()
    {
        ExecutableLookup
            .Resolve("trax-no-such-binary-zzz", _root, pathExt: null, windows: false)
            .Should()
            .BeNull();
    }

    [Test]
    public async Task A_missing_executable_is_reported_by_name()
    {
        var runner = new NodeRunner("trax-no-such-binary-zzz", _ => null);

        (await runner.IsAvailableAsync().ConfigureAwait(false)).Should().BeFalse();
        (
            await runner
                .Invoking(async r => await r.RunAsync("x.mjs", []).ConfigureAwait(false))
                .Should()
                .ThrowAsync<InvalidOperationException>()
                .ConfigureAwait(false)
        ).WithMessage("'trax-no-such-binary-zzz' was not found on PATH*--node*");
    }

    [Test]
    public void On_Windows_a_bare_name_is_tried_with_each_PATHEXT_extension()
    {
        ExecutableLookup
            .Candidates("node", ".EXE;.CMD", windows: true)
            .Should()
            .Equal("node.EXE", "node.CMD");
        ExecutableLookup
            .Candidates("node.exe", ".EXE;.CMD", windows: true)
            .Should()
            .Equal("node.exe", "node.exe.CMD");
        ExecutableLookup
            .Candidates("node", pathExt: null, windows: true)
            .Should()
            .Equal("node.COM", "node.EXE", "node.BAT", "node.CMD");
        ExecutableLookup.Candidates("node", ".EXE", windows: false).Should().Equal("node");
    }

    // A batch file runs through cmd.exe, which parses its arguments a second time (BatBadBut), so on Windows
    // only a .exe or .com is ever run. The file system is a set of paths, so these hold on every OS.

    [Test]
    public void On_Windows_the_node_exe_on_PATH_is_chosen_over_a_shim_in_an_earlier_entry()
    {
        ExecutableLookup
            .Resolve(
                "node",
                @"C:\nvm;C:\nodejs",
                ".COM;.EXE;.BAT;.CMD",
                windows: true,
                Files(@"C:\nvm\node.cmd", @"C:\nodejs\node.exe")
            )
            .Should()
            .Be(@"C:\nodejs\node.EXE");
    }

    [Test]
    public void On_Windows_a_shim_is_skipped_even_when_PATHEXT_lists_it_first()
    {
        ExecutableLookup
            .Resolve(
                "node",
                @"C:\nodejs",
                ".CMD;.BAT;.EXE",
                windows: true,
                Files(@"C:\nodejs\node.cmd", @"C:\nodejs\node.exe")
            )
            .Should()
            .Be(@"C:\nodejs\node.EXE");
    }

    [TestCase("node.cmd")]
    [TestCase("node.BAT")]
    public void On_Windows_a_shim_that_is_all_PATH_holds_is_refused_by_name(string shim)
    {
        var resolve = () =>
            ExecutableLookup.Resolve(
                "node",
                @"C:\nvm",
                pathExt: null,
                windows: true,
                Files($@"C:\nvm\{shim}")
            );

        resolve
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage(
                $@"*C:\nvm\{shim}, a batch file*real executable (.exe or .com)*--node*node.exe*"
            );
    }

    [TestCase(@"C:\nvm\node.cmd")]
    [TestCase(@"C:\nvm\node.bat")]
    [TestCase("node.cmd")]
    [TestCase("NODE.CMD")]
    public void On_Windows_a_node_option_naming_a_batch_file_is_refused(string node)
    {
        var resolve = () =>
            ExecutableLookup.Resolve(node, @"C:\nvm", pathExt: null, windows: true, Files(node));

        resolve
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("--node names a batch file*runs through cmd.exe*");
    }

    [TestCase(@"C:\nodejs\node")]
    [TestCase(@"C:\nodejs\node.ps1")]
    public void On_Windows_an_absolute_node_option_must_be_an_executable_image(string node)
    {
        var resolve = () =>
            ExecutableLookup.Resolve(node, "", pathExt: null, windows: true, Files(node));

        resolve
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("--node must name node.exe on Windows*");
    }

    [TestCase(@"C:\nodejs\node.exe")]
    [TestCase(@"C:\nodejs\NODE.EXE")]
    [TestCase(@"\\server\share\node.com")]
    public void On_Windows_an_absolute_executable_is_used_as_given(string node)
    {
        ExecutableLookup
            .Resolve(node, "", pathExt: null, windows: true, Files(node))
            .Should()
            .Be(node);
    }

    [Test]
    public void On_Windows_a_script_PATHEXT_finds_is_passed_over()
    {
        ExecutableLookup
            .Resolve(
                "node",
                @"C:\scripts",
                ".PS1;.EXE",
                windows: true,
                Files(@"C:\scripts\node.PS1")
            )
            .Should()
            .BeNull();
    }

    [Test]
    public async Task Off_Windows_a_file_named_like_a_shim_is_an_ordinary_executable()
    {
        IgnoreOnWindows();
        await Script(Path.Combine(_root, "node.cmd"), "exit 0").ConfigureAwait(false);

        ExecutableLookup
            .Resolve("node.cmd", _root, pathExt: null, windows: false)
            .Should()
            .Be(Path.Combine(_root, "node.cmd"));
    }

    [TestCase("node.exe", true)]
    [TestCase("NODE.COM", true)]
    [TestCase(@"C:\a.b\node.exe", true)]
    [TestCase("node.cmd", false)]
    [TestCase(@"C:\node.exe\node", false)]
    [TestCase("node", false)]
    public void On_Windows_only_an_exe_or_com_is_a_real_executable(string path, bool real)
    {
        ExecutableLookup.IsRealExecutable(path).Should().Be(real);
    }

    private static Func<string, bool> Files(params string[] paths)
    {
        var set = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        return set.Contains;
    }

    private static void IgnoreOnWindows()
    {
        if (OperatingSystem.IsWindows())
            Assert.Ignore("Plants a shell script; the Windows rules are pinned without one below.");
    }

    private static async Task Script(string path, string body)
    {
        await File.WriteAllTextAsync(path, $"#!/bin/sh\n{body}\n").ConfigureAwait(false);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
    }
}
