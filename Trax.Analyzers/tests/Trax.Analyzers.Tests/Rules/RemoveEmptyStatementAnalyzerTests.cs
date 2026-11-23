using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using CodeFixTest = Trax.Analyzers.Tests.Harness.CSharpCodeFixTest<
    Trax.Analyzers.Rules.RemoveEmptyStatementAnalyzer,
    Trax.Analyzers.Rules.RemoveEmptyStatementFixer>;

namespace Trax.Analyzers.Tests.Rules;

public sealed class RemoveEmptyStatementAnalyzerTests
{
    private static CodeFixTest CreateTest() => new();

    [Fact]
    public Task EmptyStatement()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test
            {
                public void A()
                {
                    {|TRX0037:;|}
                }
            }
            """;
        test.FixedCode = """
            class Test
            {
                public void A()
                {
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task EmptyInLoopStatement()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test
            {
                public void A()
                {
                    while(true)
                    {
                        {|TRX0037:;|}
                    }
                }
            }
            """;
        test.FixedCode = """
            class Test
            {
                public void A()
                {
                    while(true)
                    {
                    }
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task WhileStatement()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test
            {
                public void A()
                {
                    while(true)
                        {|TRX0037:;|}
                }
            }
            """;
        test.FixedCode = """
            class Test
            {
                public void A()
                {
                    while(true)
                    {
                    }
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ForStatement()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test
            {
                public void A()
                {
                    for(;;)
                        {|TRX0037:;|}
                }
            }
            """;
        test.FixedCode = """
            class Test
            {
                public void A()
                {
                    for(;;)
                    {
                    }
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ForEachStatement()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test
            {
                public void A()
                {
                    foreach(var a in new []{0})
                        {|TRX0037:;|}
                }
            }
            """;
        test.FixedCode = """
            class Test
            {
                public void A()
                {
                    foreach(var a in new []{0})
                    {
                    }
                }
            }
            """;

        return test.RunAsync();
    }

    [Theory]
    [InlineData("if (b)")]
    [InlineData("if (b) { } else")]
    [InlineData("while (b)")]
    [InlineData("lock (o)")]
    [InlineData("using (d)")]
    [InlineData("foreach (var (x, y) in items)")]
    public Task EmbeddedStatement(string statement)
    {
        var test = CreateTest();
        test.TestCode = $$"""
            class Test
            {
                public void A(bool b, object o, System.IDisposable d, (int, int)[] items)
                {
                    {{statement}}
                        {|TRX0037:;|}
                }
            }
            """;
        test.FixedCode = $$"""
            class Test
            {
                public void A(bool b, object o, System.IDisposable d, (int, int)[] items)
                {
                    {{statement}}
                    {
                    }
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task EmbeddedStatement_SameLine()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test
            {
                public void A(bool b)
                {
                    if (b) {|TRX0037:;|}
                }
            }
            """;
        test.FixedCode = """
            class Test
            {
                public void A(bool b)
                {
                    if (b)
                    {
                    }
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task DoStatement()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test
            {
                public void A(bool b)
                {
                    do
                        {|TRX0037:;|}
                    while (b);
                }
            }
            """;
        test.FixedCode = """
            class Test
            {
                public void A(bool b)
                {
                    do
                    {
                    }
                    while (b);
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task FixedStatement()
    {
        var test = CreateTest();
        test.SolutionTransforms.Add(static (solution, projectId) =>
            solution.WithProjectCompilationOptions(projectId, ((CSharpCompilationOptions)solution.GetProject(projectId)!.CompilationOptions!).WithAllowUnsafe(true)));
        test.TestCode = """
            unsafe class Test
            {
                public void A(int[] array)
                {
                    fixed (int* p = array)
                        {|TRX0037:;|}
                }
            }
            """;
        test.FixedCode = """
            unsafe class Test
            {
                public void A(int[] array)
                {
                    fixed (int* p = array)
                    {
                    }
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task EmptyStatementInSwitchSection()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test
            {
                public void A(int value)
                {
                    switch (value)
                    {
                        case 0:
                            {|TRX0037:;|}
                            break;
                    }
                }
            }
            """;
        test.FixedCode = """
            class Test
            {
                public void A(int value)
                {
                    switch (value)
                    {
                        case 0:
                            break;
                    }
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task EmptyStatementInTopLevelStatements()
    {
        var test = CreateTest();
        test.TestState.OutputKind = OutputKind.ConsoleApplication;
        test.TestCode = """
            System.Console.WriteLine();
            {|TRX0037:;|}
            """;
        test.FixedCode = """
            System.Console.WriteLine();

            """;

        return test.RunAsync();
    }

    [Fact]
    public Task EmptyStatementInALabel()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test
            {
                public void A()
                {
            test:
                    ;
                }
            }
            """;

        return test.RunAsync();
    }
}
