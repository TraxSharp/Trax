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
    private string _root = null!;
    private string _previousDirectory = null!;

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
    public void A_node_planted_in_the_current_directory_never_runs()
    {
        IgnoreOnWindows();
        var checkout = Directory.CreateDirectory(Path.Combine(_root, "checkout")).FullName;
        var marker = Path.Combine(_root, "planted-node-ran");
        Script(Path.Combine(checkout, "node"), $"touch '{marker}'");
        Directory.SetCurrentDirectory(checkout);

        // The empty entry and '.' are how a PATH says "the current directory".
        var runner = new NodeRunner(
            "node",
            name => ExecutableLookup.Resolve(name, ":.:checkout", null, windows: false)
        );

        runner.IsAvailable().Should().BeFalse("no absolute PATH entry holds a node");
        File.Exists(marker)
            .Should()
            .BeFalse(
                "the node in the current directory must not be run. See "
                    + "docs/adr/0001-the-machine-toolchain-is-half-in-process.md."
            );
    }

    [Test]
    public void The_node_on_PATH_runs_instead_of_one_in_the_current_directory()
    {
        IgnoreOnWindows();
        var checkout = Directory.CreateDirectory(Path.Combine(_root, "checkout")).FullName;
        var bin = Directory.CreateDirectory(Path.Combine(_root, "bin")).FullName;
        var marker = Path.Combine(_root, "planted-node-ran");
        Script(Path.Combine(checkout, "node"), $"touch '{marker}'");
        Script(Path.Combine(bin, "node"), "echo v24.0.0");
        Directory.SetCurrentDirectory(checkout);

        var runner = new NodeRunner(
            "node",
            name => ExecutableLookup.Resolve(name, $".:{bin}", null, windows: false)
        );

        runner.IsAvailable().Should().BeTrue();
        runner.Run("--version", []).StdOut.Trim().Should().Be("v24.0.0");
        File.Exists(marker).Should().BeFalse();
    }

    [Test]
    public void The_real_lookup_does_not_resolve_to_the_current_directory()
    {
        IgnoreOnWindows();
        var checkout = Directory.CreateDirectory(Path.Combine(_root, "checkout")).FullName;
        var planted = Path.Combine(checkout, "node");
        Script(planted, "exit 0");
        Directory.SetCurrentDirectory(checkout);

        ExecutableLookup.Resolve("node").Should().NotBe(planted).And.NotBe("node");
    }

    [Test]
    public void A_file_without_an_execute_bit_is_skipped()
    {
        IgnoreOnWindows();
        var first = Directory.CreateDirectory(Path.Combine(_root, "first")).FullName;
        var second = Directory.CreateDirectory(Path.Combine(_root, "second")).FullName;
        File.WriteAllText(Path.Combine(first, "node"), "not executable");
        Script(Path.Combine(second, "node"), "exit 0");

        ExecutableLookup
            .Resolve("node", $"{first}:{second}", null, windows: false)
            .Should()
            .Be(Path.Combine(second, "node"));
    }

    [Test]
    public void An_absolute_path_is_used_as_given_and_a_missing_one_is_not_found()
    {
        IgnoreOnWindows();
        var node = Path.Combine(_root, "node");
        Script(node, "exit 0");

        ExecutableLookup.Resolve(node, "", null, windows: false).Should().Be(node);
        ExecutableLookup
            .Resolve(Path.Combine(_root, "absent"), "", null, windows: false)
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
        var resolve = () => ExecutableLookup.Resolve(command, "/usr/bin", null, windows);

        resolve
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*--node must be an absolute path or a command name found on PATH*");
    }

    [Test]
    public void A_name_not_on_PATH_is_not_found()
    {
        ExecutableLookup
            .Resolve("trax-no-such-binary-zzz", _root, null, windows: false)
            .Should()
            .BeNull();
    }

    [Test]
    public void A_missing_executable_is_reported_by_name()
    {
        var runner = new NodeRunner("trax-no-such-binary-zzz", _ => null);

        runner.IsAvailable().Should().BeFalse();
        runner
            .Invoking(r => r.Run("x.mjs", []))
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("'trax-no-such-binary-zzz' was not found on PATH*--node*");
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
            .Candidates("node", null, windows: true)
            .Should()
            .Equal("node.COM", "node.EXE", "node.BAT", "node.CMD");
        ExecutableLookup.Candidates("node", ".EXE", windows: false).Should().Equal("node");
    }

    private static void IgnoreOnWindows()
    {
        if (OperatingSystem.IsWindows())
            Assert.Ignore("Plants a shell script; the Windows rules are pinned without one below.");
    }

    private static void Script(string path, string body)
    {
        File.WriteAllText(path, $"#!/bin/sh\n{body}\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
    }
}
