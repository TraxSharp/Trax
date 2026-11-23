using Microsoft.CodeAnalysis;
using RefactoringTest = Trax.Analyzers.Tests.Harness.CSharpCodeRefactoringTest<
    Trax.Analyzers.Refactorings.MakeInterpolatedStringRefactoring>;

namespace Trax.Analyzers.Tests.Refactorings;

public sealed class MakeInterpolatedStringRefactoringTests
{
    private static RefactoringTest CreateTest()
    {
        var test = new RefactoringTest();
        test.TestState.OutputKind = OutputKind.ConsoleApplication;
        return test;
    }

    [Fact]
    public Task SimpleString()
    {
        var test = CreateTest();
        test.TestCode = """_ = [|"test"|];""";
        test.FixedCode = """_ = $"test";""";

        return test.RunAsync();
    }

    [Fact]
    public Task VerbatimString()
    {
        var test = CreateTest();
        test.TestCode = """_ = [|@"test"|];""";
        test.FixedCode = """_ = $@"test";""";

        return test.RunAsync();
    }

    [Fact]
    public Task SimpleStringWithOpenAndCloseCurlyBraces()
    {
        var test = CreateTest();
        test.TestCode = """_ = [|"test{0}"|];""";
        test.FixedCode = """_ = $"test{0}";""";

        return test.RunAsync();
    }

    [Fact]
    public Task VerbatimStringWithOpenAndCloseCurlyBraces()
    {
        var test = CreateTest();
        test.TestCode = """_ = [|@"test{0}"|];""";
        test.FixedCode = """_ = $@"test{0}";""";

        return test.RunAsync();
    }
}
