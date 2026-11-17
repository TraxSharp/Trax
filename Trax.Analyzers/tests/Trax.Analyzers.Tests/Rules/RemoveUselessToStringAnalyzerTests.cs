using CodeFixTest = Trax.Analyzers.Tests.Harness.CSharpCodeFixTest<
    Trax.Analyzers.Rules.RemoveUselessToStringAnalyzer,
    Trax.Analyzers.Rules.RemoveUselessToStringFixer>;

namespace Trax.Analyzers.Tests.Rules;

public sealed class RemoveUselessToStringAnalyzerTests
{
    private static CodeFixTest CreateTest() => new();

    [Fact]
    public Task IntToString_ShouldNotReportDiagnostic()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test
            {
                public void A() => 1.ToString();
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task StringToString_ShouldReportDiagnostic()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test
            {
                public string A() => {|TRX0044:"".ToString()|};
            }
            """;
        test.FixedCode = """
            class Test
            {
                public string A() => "";
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task StringToStringWithSideEffectingProvider_ShouldNotOfferFix()
    {
        const string SourceCode = """
            class Test
            {
                static int calls;
                static System.IFormatProvider Provider() { calls++; return null; }

                public string A() => {|TRX0044:"".ToString(Provider())|};
            }
            """;
        var test = CreateTest();
        test.TestCode = SourceCode;
        test.FixedCode = SourceCode;

        return test.RunAsync();
    }

    [Fact]
    public Task StringToStringWithPropertyProvider_ShouldNotOfferFix()
    {
        const string SourceCode = """
            class Test
            {
                static System.IFormatProvider Provider => null;

                public string A() => {|TRX0044:"".ToString(Provider)|};
            }
            """;
        var test = CreateTest();
        test.TestCode = SourceCode;
        test.FixedCode = SourceCode;

        return test.RunAsync();
    }

    [Fact]
    public Task StringToStringWithInvariantCulture_ShouldReportDiagnostic()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test
            {
                public string A() => {|TRX0044:"".ToString(System.Globalization.CultureInfo.InvariantCulture)|};
            }
            """;
        test.FixedCode = """
            class Test
            {
                public string A() => "";
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task StringToStringWithNullProvider_ShouldReportDiagnostic()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test
            {
                public string A() => {|TRX0044:"".ToString(null)|};
            }
            """;
        test.FixedCode = """
            class Test
            {
                public string A() => "";
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task StringToStringWithParameterProvider_ShouldReportDiagnostic()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test
            {
                public string A(System.IFormatProvider provider) => {|TRX0044:"".ToString(provider)|};
            }
            """;
        test.FixedCode = """
            class Test
            {
                public string A(System.IFormatProvider provider) => "";
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task StringToStringConditionalAccess_ShouldNotOfferFix()
    {
        const string SourceCode = """
            class Test
            {
                public string A(string value) => value?{|TRX0044:.ToString()|};
            }
            """;
        var test = CreateTest();
        test.TestCode = SourceCode;
        test.FixedCode = SourceCode;

        return test.RunAsync();
    }

    [Fact]
    public Task ExpressionStatement_NoCodeFix()
    {
        const string Source = """
            class Sample
            {
                void A(string value) => {|TRX0044:value.ToString()|};

                void B(string value)
                {
                    {|TRX0044:value.ToString()|};
                }
            }
            """;
        var test = CreateTest();
        test.TestCode = Source;
        test.FixedCode = Source;

        return test.RunAsync();
    }
}
