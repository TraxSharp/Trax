using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using CodeFixTest = Trax.Analyzers.Tests.Harness.CSharpCodeFixTest<
    Trax.Analyzers.Rules.ILoggerParameterTypeShouldMatchContainingTypeAnalyzer,
    Trax.Analyzers.Rules.ILoggerParameterTypeShouldMatchContainingTypeFixer>;

namespace Trax.Analyzers.Tests.Rules;

public sealed class ILoggerParameterTypeShouldMatchContainingTypeAnalyzerTests
{
    private static CodeFixTest CreateTest()
    {
        var test = new CodeFixTest();
        test.ReferenceAssemblies = test.ReferenceAssemblies.AddLoggingAbstractions();
        return test;
    }

    [Fact]
    public Task PrimaryConstructor_Mismatch_ShouldReportDiagnostic()
    {
        var test = CreateTest();
        test.TestCode = """
            using Microsoft.Extensions.Logging;

            class A({|TRX0180:ILogger<B>|} logger)
            {
            }

            class B
            {
            }
            """;
        test.FixedCode = """
            using Microsoft.Extensions.Logging;

            class A(ILogger<A> logger)
            {
            }

            class B
            {
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task RegularConstructor_Mismatch_ShouldReportDiagnostic()
    {
        var test = CreateTest();
        test.TestCode = """
            using Microsoft.Extensions.Logging;

            class A
            {
                public A({|TRX0180:ILogger<B>|} logger)
                {
                }
            }

            class B
            {
            }
            """;
        test.FixedCode = """
            using Microsoft.Extensions.Logging;

            class A
            {
                public A(ILogger<A> logger)
                {
                }
            }

            class B
            {
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task PrimaryConstructor_Match_ShouldNotReportDiagnostic()
    {
        var test = CreateTest();
        test.TestCode = """
            using Microsoft.Extensions.Logging;

            class A(ILogger<A> logger)
            {
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task RegularConstructor_Match_ShouldNotReportDiagnostic()
    {
        var test = CreateTest();
        test.TestCode = """
            using Microsoft.Extensions.Logging;

            class A
            {
                public A(ILogger<A> logger)
                {
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task NonGenericILogger_ShouldNotReportDiagnostic()
    {
        var test = CreateTest();
        test.TestCode = """
            using Microsoft.Extensions.Logging;

            class A(ILogger logger)
            {
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task AbstractClass_ShouldNotReportDiagnostic()
    {
        var test = CreateTest();
        test.TestCode = """
            using Microsoft.Extensions.Logging;

            abstract class A(ILogger<B> logger)
            {
            }

            class B
            {
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task Interface_ShouldNotReportDiagnostic()
    {
        var test = CreateTest();
        test.TestCode = """
            using Microsoft.Extensions.Logging;

            interface IA
            {
            }

            class B
            {
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task MultipleConstructors_Mismatch_ShouldReportDiagnostic()
    {
        var test = CreateTest();
        test.TestCode = """
            using Microsoft.Extensions.Logging;

            class A
            {
                public A({|TRX0180:ILogger<B>|} logger)
                {
                }

                public A(string name, {|TRX0180:ILogger<B>|} logger)
                {
                }
            }

            class B
            {
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task NestedClass_Mismatch_ShouldReportDiagnostic()
    {
        var test = CreateTest();
        test.TestCode = """
            using Microsoft.Extensions.Logging;

            class Outer
            {
                class Inner({|TRX0180:ILogger<Outer>|} logger)
                {
                }
            }
            """;
        test.FixedCode = """
            using Microsoft.Extensions.Logging;

            class Outer
            {
                class Inner(ILogger<Inner> logger)
                {
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task GenericClass_Mismatch_ShouldReportDiagnostic()
    {
        var test = CreateTest();
        test.TestCode = """
            using Microsoft.Extensions.Logging;

            class A<T>({|TRX0180:ILogger<B>|} logger)
            {
            }

            class B
            {
            }
            """;
        test.FixedCode = """
            using Microsoft.Extensions.Logging;

            class A<T>(ILogger<A<T>> logger)
            {
            }

            class B
            {
            }
            """;

        return test.RunAsync();
    }
}
