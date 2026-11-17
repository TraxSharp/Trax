using CodeFixTest = Trax.Analyzers.Tests.Harness.CSharpCodeFixTest<
    Trax.Analyzers.Rules.ExceptionNameShouldEndWithExceptionAnalyzer,
    Trax.Analyzers.Rules.TypeNameShouldEndWithSuffixFixer>;

namespace Trax.Analyzers.Tests.Rules;

public sealed class ExceptionNameShouldEndWithExceptionAnalyzerTests
{
    private static CodeFixTest CreateTest() => new();

    [Fact]
    public Task NameEndsWithException()
    {
        var test = CreateTest();
        test.TestCode = """
            class CustomException : System.Exception
            {
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task NameDoesNotEndWithAttribute()
    {
        var test = CreateTest();
        test.TestCode = """
            class {|TRX0058:CustomEx|} : System.Exception
            {
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task NameDoesNotEndWithAttribute_CodeFix()
    {
        var test = CreateTest();
        test.TestCode = """
            class {|TRX0058:CustomEx|} : System.Exception
            {
            }
            """;
        test.FixedCode = """
            class CustomExException : System.Exception
            {
            }
            """;

        return test.RunAsync();
    }
}
