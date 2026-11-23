using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using CodeFixTest = Trax.Analyzers.Tests.Harness.CSharpCodeFixTest<
    Trax.Analyzers.Rules.TaskInUsingAnalyzer,
    Trax.Analyzers.Rules.TaskInUsingFixer>;

namespace Trax.Analyzers.Tests.Rules;

public sealed class TaskInUsingAnalyzerTests
{
    private static CodeFixTest CreateTest()
    {
        var test = new CodeFixTest();
        test.TestState.OutputKind = OutputKind.ConsoleApplication;
        return test;
    }

    [Fact]
    public Task SingleTaskInUsing()
    {
        var test = CreateTest();
        test.TestCode = """
            using System.Threading.Tasks;

            Task t = null;
            using ({|TRX0129:t|}) { }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task SingleTaskInUsing_CodeFix()
    {
        var test = CreateTest();
        test.TestCode = """
            using System.Threading.Tasks;

            Task<System.IDisposable> t = null;
            using ({|TRX0129:t|}) { }
            """;
        test.FixedCode = """
            using System.Threading.Tasks;

            Task<System.IDisposable> t = null;
            using (await t) { }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task TaskOfNonDisposableInUsing_NoCodeFix()
    {
        var test = CreateTest();
        test.TestCode = """
            using System;
            using System.Threading.Tasks;

            class Dummy
            {
            }

            class Test
            {
                static void Main() { }

                async Task A(IDisposable disposable)
                {
                    Task<Dummy> t = null;
                    using (disposable)
                    {
                        using (var d = {|TRX0129:t|}) { await Task.Yield(); }
                    }
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task TaskOfDisposableInUsing_CodeFix()
    {
        var test = CreateTest();
        test.TestCode = """
            using System;
            using System.Threading.Tasks;

            class Dummy : IDisposable
            {
                public void Dispose()
                {
                }
            }

            class Test
            {
                static void Main() { }

                async Task A(IDisposable disposable)
                {
                    Task<Dummy> t = null;
                    using (disposable)
                    {
                        using (var d = {|TRX0129:t|}) { await Task.Yield(); }
                    }
                }
            }
            """;
        test.FixedCode = """
            using System;
            using System.Threading.Tasks;

            class Dummy : IDisposable
            {
                public void Dispose()
                {
                }
            }

            class Test
            {
                static void Main() { }

                async Task A(IDisposable disposable)
                {
                    Task<Dummy> t = null;
                    using (disposable)
                    {
                        using (var d = await t) { await Task.Yield(); }
                    }
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task SingleTaskAssignedInUsing()
    {
        var test = CreateTest();
        test.TestCode = """
            using System.Threading.Tasks;

            Task t = null;
            using (var a = {|TRX0129:t|}) { }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task MultipleTasksInUsing()
    {
        var test = CreateTest();
        test.TestCode = """
            using System.Threading.Tasks;

            Task t1 = null;
            Task t2 = null;
            using (Task a = {|TRX0129:t1|}, b = {|TRX0129:t2|}) { }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task TaskOfTInUsing()
    {
        var test = CreateTest();
        test.TestCode = """
            using System.Threading.Tasks;

            Task<System.IDisposable> t1 = null;
            using ({|TRX0129:t1|}) { }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task TaskOfTInUsingStatement()
    {
        var test = CreateTest();
        test.TestCode = """
            using System.Threading.Tasks;

            Task<System.IDisposable> t1 = null;
            using var a = {|TRX0129:t1|};
            """;

        return test.RunAsync();
    }
}
