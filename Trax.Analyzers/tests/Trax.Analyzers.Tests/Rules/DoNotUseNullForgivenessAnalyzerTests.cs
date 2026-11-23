using Microsoft.CodeAnalysis.Testing;
using AnalyzerTest = Trax.Analyzers.Tests.Harness.CSharpAnalyzerTest<
    Trax.Analyzers.Rules.DoNotUseNullForgivenessAnalyzer>;

namespace Trax.Analyzers.Tests.Rules;

public sealed class DoNotUseNullForgivenessAnalyzerTests
{
    private static AnalyzerTest CreateTest() => new();

    [Fact]
    public Task NullForgiveness_NullLiteral_ReportsDiagnostic()
    {
        var test = CreateTest();
        test.TestCode = """
            #nullable enable
            class Sample
            {
                string _field = {|TRX0191:null!|};
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task NullForgiveness_DefaultLiteral_ReportsDiagnostic()
    {
        var test = CreateTest();
        test.TestCode = """
            #nullable enable
            class Sample
            {
                string _field = {|TRX0191:default!|};
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task NullForgiveness_DefaultExpression_ReportsDiagnostic()
    {
        var test = CreateTest();
        test.TestCode = """
            #nullable enable
            class Sample
            {
                string _field = {|TRX0191:default(string)!|};
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task NullForgiveness_Property_ReportsDiagnostic()
    {
        var test = CreateTest();
        test.TestCode = """
            #nullable enable
            class Sample
            {
                string Prop { get; set; } = {|TRX0191:null!|};
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task NullForgiveness_VariableAssignment_ReportsDiagnostic()
    {
        var test = CreateTest();
        test.TestCode = """
            #nullable enable
            class Sample
            {
                void M()
                {
                    string s = {|TRX0191:null!|};
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task NullForgiveness_MemberAccess_NoDiagnostic()
    {
        var test = CreateTest();
        test.TestCode = """
            #nullable enable
            class Model
            {
                public string? Value { get; set; }
            }
            class Sample
            {
                void M(Model model)
                {
                    _ = model.Value!.Length;
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task NoNullForgiveness_NoDiagnostic()
    {
        var test = CreateTest();
        test.TestCode = """
            #nullable enable
            class Sample
            {
                string _field = "value";
                string Prop { get; set; } = "value";
            }
            """;

        return test.RunAsync();
    }
}
