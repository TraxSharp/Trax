using Microsoft.CodeAnalysis;
using DiagnosticResult = Microsoft.CodeAnalysis.Testing.DiagnosticResult;
using AnalyzerTest = Trax.Analyzers.Tests.Harness.CSharpAnalyzerTest<
    Trax.Analyzers.Rules.ArgumentExceptionShouldSpecifyArgumentNameAnalyzer>;
using CodeFixTest = Trax.Analyzers.Tests.Harness.CSharpCodeFixTest<
    Trax.Analyzers.Rules.ArgumentExceptionShouldSpecifyArgumentNameAnalyzer,
    Trax.Analyzers.Rules.ArgumentExceptionShouldSpecifyArgumentNameFixer>;

namespace Trax.Analyzers.Tests.Rules;

public sealed class ArgumentExceptionShouldSpecifyArgumentNameAnalyzerTests
{
    [Fact]
    public Task ArgumentNameIsSpecified_Record_ShouldNotReportError()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            using System;
            internal sealed record ManuscriptId(int Id)
            {
                public int Id { get; } = Id > 0 ? Id : throw new ArgumentOutOfRangeException(paramName: nameof(Id), Id, message: "Must be greater than 0");
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ArgumentNameIsSpecified_LocalFunction_ShouldNotReportError()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            class Sample
            {
                void Test(string test)
                {
                    void LocalFunction(string a)
                    {
                        throw new System.ArgumentNullException(nameof(a));
                    }
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ArgumentNameIsSpecified_LocalFunction_Static_ShouldNotReportError()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            class Sample
            {
                void Test(string test)
                {
                    static void LocalFunction(string a)
                    {
                        throw new System.ArgumentNullException({|TRX0015:"test"|});
                    }
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ArgumentNameIsSpecified_LocalFunction_ArgumentFromParentMethod_ShouldNotReportError()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            class Sample
            {
                void Test(string test)
                {
                    void LocalFunction()
                    {
                        throw new System.ArgumentNullException(nameof(test));
                    }
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ArgumentNameIsSpecified_Operator_ShouldNotReportError()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            class Sample
            {
                public static Sample operator +(Sample first, Sample second)
                {
                    throw new System.ArgumentNullException(nameof(first));
                    throw new System.ArgumentNullException(nameof(second));
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ArgumentNameIsSpecified_Method_ShouldNotReportError()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            class Sample
            {
                Sample(string test)
                {
                    throw new System.Exception();
                    throw new System.ArgumentException("message", nameof(test));
                    throw new System.ArgumentNullException(nameof(test));
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ArgumentNameIsSpecified_Indexer_ShouldNotReportError()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            class Sample
            {
                string this[int index]
                {
                    get { throw new System.ArgumentNullException(nameof(index)); }
                    set { throw new System.ArgumentNullException(nameof(index)); }
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ArgumentNameIsSpecified_Setter_ShouldNotReportError()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            class Sample
            {
                string Prop
                {
                    get { throw null; }
                    set { throw new System.ArgumentNullException(nameof(value)); }
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ArgumentNameDoesNotMatchAParameter_Properties_ShouldReportError()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            class TestAttribute
            {
                string Prop
                {
                    get { throw null; }
                    set { throw new System.ArgumentNullException({|#0:"unknown"|}); }
                }
            }
            """;
        test.ExpectedDiagnostics.Add(new DiagnosticResult("TRX0015", DiagnosticSeverity.Warning).WithLocation(0).WithMessage("'unknown' is not a valid parameter name"));

        return test.RunAsync();
    }

    [Fact]
    public Task ArgumentNameDoesNotMatchAParameter_Methods_ShouldReportError()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            class TestAttribute
            {
                void Test(string test)
                {
                    throw new System.ArgumentException("message", {|#0:"unknown"|});
                }
            }
            """;
        test.ExpectedDiagnostics.Add(new DiagnosticResult("TRX0015", DiagnosticSeverity.Warning).WithLocation(0).WithMessage("'unknown' is not a valid parameter name"));

        return test.RunAsync();
    }

    [Fact]
    public Task OverloadWithoutParameterName_Properties_ShouldReportError()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            class TestAttribute
            {
                string Prop
                {
                    get { throw null; }
                    set { throw {|TRX0015:new System.ArgumentNullException()|}; }
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task OverloadWithoutParameterName_Methods_ShouldReportError()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            class TestAttribute
            {
                void Test(string test)
                {
                    throw {|TRX0015:new System.ArgumentException("message")|};
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ValidParameterName_Lambda()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            class TestAttribute
            {
                void Test(string test)
                {
                    _ = new System.Action<int>((int a) =>
                    {
                        throw new System.ArgumentOutOfRangeException(paramName: nameof(a), a, message: "address out of range");
                        throw new System.ArgumentOutOfRangeException(paramName: nameof(test), a, message: "address out of range");
                    });
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task InvalidParameterName_Lambda()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            class TestAttribute
            {
                void Test(string test)
                {
                    _ = new System.Action<int>((int a) =>
                    {
                        if (a < 0)
                            throw new System.ArgumentOutOfRangeException(paramName: {|TRX0015:"dummy"|}, a, message: "address out of range");
                    });
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task InvalidParameterName_StaticLambda()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            class TestAttribute
            {
                void Test(string test)
                {
                    _ = new System.Action<int>(static (int a) =>
                    {
                        if (a < 0)
                            throw new System.ArgumentOutOfRangeException(paramName: {|TRX0015:"test"|}, a, message: "address out of range");
                    });
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ValidParameterName_LambdaWithoutParentheses()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            class TestAttribute
            {
                void Test(string test)
                {
                    _ = new System.Action<int>(a =>
                    {
                        throw new System.ArgumentOutOfRangeException(paramName: nameof(a), a, message: "address out of range");
                    });
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ValidParameterName_StaticLambdaWithoutParameter()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            class TestAttribute
            {
                void Test(string test)
                {
                    _ = new System.Action(static () =>
                    {
                        throw new System.ArgumentNullException({|TRX0015:"test"|});
                    });
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task InvalidParameterName_Delegate()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            class TestAttribute
            {
                void Test(string test)
                {
                    _ = new System.Action<int>(delegate (int a)
                    {
                        if (a < 0)
                            throw new System.ArgumentOutOfRangeException(paramName: {|TRX0015:"dummy"|}, a, message: "address out of range");
                    });
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ValidParameterName_Delegate()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            class TestAttribute
            {
                void Test(string test)
                {
                    _ = new System.Action<int>(delegate (int a)
                    {
                        if (a < 0)
                            throw new System.ArgumentOutOfRangeException(paramName: nameof(a), a, message: "address out of range");
                    });
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task InvalidParameterName_StaticDelegate()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            class TestAttribute
            {
                void Test(string test)
                {
                    _ = new System.Action<int>(static delegate (int a)
                    {
                        throw new System.ArgumentOutOfRangeException(paramName: {|TRX0015:"test"|}, a, message: "address out of range");
                    });
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task InvalidParameterName_StaticLocalFunction_NameofParameterOfParentMethod()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            class Sample
            {
                void Test(string test)
                {
                    static void LocalFunction(string a)
                    {
                        throw new System.ArgumentNullException({|TRX0015:nameof(test)|});
                    }
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task InvalidParameterName_StaticLambda_NameofParameterOfParentMethod()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            class Sample
            {
                void Test(string test)
                {
                    _ = new System.Action<int>(static a =>
                    {
                        throw new System.ArgumentOutOfRangeException(paramName: {|TRX0015:nameof(test)|}, a, message: "address out of range");
                    });
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ValidParameterName_NestedLambda_NameofParameterOfParentLambda()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            class Sample
            {
                void Test()
                {
                    _ = new System.Action<int>(static a =>
                    {
                        _ = new System.Action<int>(b =>
                        {
                            throw new System.ArgumentOutOfRangeException(paramName: nameof(a), b, message: "address out of range");
                        });
                    });
                }
            }
            """;

        return test.RunAsync();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("te st")]
    [InlineData("test ")]
    [InlineData("1test")]
    public Task InvalidParameterName_NotAnIdentifier(string value)
    {
        var test = new AnalyzerTest();
        test.TestCode = $$"""
            class Sample
            {
                void Test(string test)
                {
                    throw new System.ArgumentNullException({|TRX0015:"{{value}}"|});
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task UseNameof_ParameterNamedAfterKeyword()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            class Sample
            {
                void Test(string @class)
                {
                    throw new System.ArgumentNullException({|TRX0043:"class"|});
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task NoCtorWithParameterName()
    {
        var test = new AnalyzerTest();
        test.TestState.OutputKind = OutputKind.ConsoleApplication;
        test.TestCode = """
            using System;

            void Sample1(string str)
            {
                throw new CustomArgumentException("Message");
            }

            void Sample2(string str)
            {
                throw new CustomArgumentException("Message", new InvalidOperationException());
            }

            public class CustomArgumentException : ArgumentException
            {
                public CustomArgumentException(string message)
                    : base(message)
                {
                }

                public CustomArgumentException(string message, Exception cause)
                    : base(message, cause)
                {
                }

                public CustomArgumentException(string message, string description, Exception cause)
                    : base(message, cause)
                {
                }
             }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task PrimaryConstructor()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            using System;

            public class Sample(string id)
            {
                void A() => throw new ArgumentException("", nameof(id));
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ThrowIfNull_ValidParameter_ShouldNotReportError()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            using System;
            class Sample
            {
                void Test(string test)
                {
                    ArgumentNullException.ThrowIfNull(test);
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ThrowIfNull_InvalidParameter_ShouldReportError()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            using System;
            class Sample
            {
                void Test(string test)
                {
                    ArgumentNullException.ThrowIfNull({|TRX0015:Name|});
                }

                public static string Name { get; }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ThrowIfNullOrEmpty_ValidParameter_ShouldNotReportError()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            using System;
            class Sample
            {
                void Test(string test)
                {
                    ArgumentNullException.ThrowIfNullOrEmpty(test);
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ThrowIfNullOrEmpty_InvalidParameter_ShouldReportError()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            using System;
            class Sample
            {
                void Test(string test)
                {
                    ArgumentNullException.ThrowIfNullOrEmpty({|TRX0015:Name|});
                }

                public static string Name { get; }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ThrowIfNullOrWhiteSpace_ValidParameter_ShouldNotReportError()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            using System;
            class Sample
            {
                void Test(string test)
                {
                    ArgumentNullException.ThrowIfNullOrWhiteSpace(test);
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ThrowIfNullOrWhiteSpace_InvalidParameter_ShouldReportError()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            using System;
            class Sample
            {
                void Test(string test)
                {
                    ArgumentNullException.ThrowIfNullOrWhiteSpace({|TRX0015:Name|});
                }

                public static string Name { get; }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ArgumentException_ThrowIfNullOrEmpty_ValidParameter_ShouldNotReportError()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            using System;
            class Sample
            {
                void Test(string test)
                {
                    ArgumentException.ThrowIfNullOrEmpty(test);
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ArgumentException_ThrowIfNullOrEmpty_InvalidParameter_ShouldReportError()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            using System;
            class Sample
            {
                void Test(string test)
                {
                    ArgumentException.ThrowIfNullOrEmpty({|TRX0015:Name|});
                }

                public static string Name { get; }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ArgumentException_ThrowIfNullOrWhiteSpace_ValidParameter_ShouldNotReportError()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            using System;
            class Sample
            {
                void Test(string test)
                {
                    ArgumentException.ThrowIfNullOrWhiteSpace(test);
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ArgumentException_ThrowIfNullOrWhiteSpace_InvalidParameter_ShouldReportError()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            using System;
            class Sample
            {
                void Test(string test)
                {
                    ArgumentException.ThrowIfNullOrWhiteSpace({|TRX0015:Name|});
                }

                public static string Name { get; }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ThrowIfNull_WithValidParamNameArgument_ShouldNotReportError()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            using System;
            class Sample
            {
                void Test(string test)
                {
                    ArgumentNullException.ThrowIfNull(test, nameof(test));
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ThrowIfNull_WithInvalidParamNameArgument_ShouldReportError()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            using System;
            class Sample
            {
                void Test(string test)
                {
                    ArgumentNullException.ThrowIfNull(test, {|#0:"invalid"|});
                }
            }
            """;
        test.ExpectedDiagnostics.Add(new DiagnosticResult("TRX0015", DiagnosticSeverity.Warning).WithLocation(0).WithMessage("'invalid' is not a valid parameter name"));

        return test.RunAsync();
    }

    [Fact]
    public Task ThrowIfNull_StaticLocalFunction_NameofParameterOfParentMethod_ShouldReportError()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            using System;
            class Sample
            {
                void Test(string test)
                {
                    static void LocalFunction(string a)
                    {
                        ArgumentNullException.ThrowIfNull(a, {|TRX0015:nameof(test)|});
                    }
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ArgumentException_ThrowIfNullOrEmpty_WithValidParamNameArgument_ShouldNotReportError()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            using System;
            class Sample
            {
                void Test(string test)
                {
                    ArgumentException.ThrowIfNullOrEmpty(test, nameof(test));
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ArgumentException_ThrowIfNullOrEmpty_WithInvalidParamNameArgument_ShouldReportError()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            using System;
            class Sample
            {
                void Test(string test)
                {
                    ArgumentException.ThrowIfNullOrEmpty(test, {|#0:"invalid"|});
                }
            }
            """;
        test.ExpectedDiagnostics.Add(new DiagnosticResult("TRX0015", DiagnosticSeverity.Warning).WithLocation(0).WithMessage("'invalid' is not a valid parameter name"));

        return test.RunAsync();
    }

    [Fact]
    public Task ArgumentOutOfRangeException_ThrowIfNegative_ValidParameter_ShouldNotReportError()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            using System;
            class Sample
            {
                void Test(int value)
                {
                    ArgumentOutOfRangeException.ThrowIfNegative(value);
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ArgumentOutOfRangeException_ThrowIfNegative_InvalidParameter_ShouldReportError()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            using System;
            class Sample
            {
                void Test(int value)
                {
                    ArgumentOutOfRangeException.ThrowIfNegative({|TRX0015:Count|});
                }

                public static int Count { get; }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ArgumentOutOfRangeException_ThrowIfNegativeOrZero_ValidParameter_ShouldNotReportError()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            using System;
            class Sample
            {
                void Test(int value)
                {
                    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ArgumentOutOfRangeException_ThrowIfGreaterThan_ValidParameter_ShouldNotReportError()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            using System;
            class Sample
            {
                void Test(int value)
                {
                    ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 100);
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ArgumentOutOfRangeException_ThrowIfGreaterThanOrEqual_InvalidParameter_ShouldReportError()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            using System;
            class Sample
            {
                void Test(int value)
                {
                    ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual({|TRX0015:MaxValue|}, 100);
                }

                public static int MaxValue { get; }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ThrowIfNull_WithNullExpression_ShouldReportError()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            using System;
            class Sample
            {
                void Test(string test)
                {
                    ArgumentNullException.ThrowIfNull({|TRX0015:""|});
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ThrowIfNull_WithNullExpressionAndValidParamName_ShouldNotReportError()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            using System;
            class Sample
            {
                void Test(string test)
                {
                    ArgumentNullException.ThrowIfNull("", nameof(test));
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ThrowIfNull_WithNullExpressionAndInvalidParamName_ShouldReportError()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            using System;
            class Sample
            {
                void Test(string test)
                {
                    ArgumentNullException.ThrowIfNull("", {|TRX0015:"invalid"|});
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ThrowIfNull_WithBooleanExpression_ShouldReportError()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            using System;
            class Sample
            {
                void Test(string test)
                {
                    ArgumentNullException.ThrowIfNull({|#0:0 == 1|});
                }
            }
            """;
        test.ExpectedDiagnostics.Add(new DiagnosticResult("TRX0015", DiagnosticSeverity.Warning).WithLocation(0).WithMessage("The expression does not match a parameter"));

        return test.RunAsync();
    }

    [Fact]
    public Task ThrowIfNull_WithBooleanExpressionAndValidParamName_ShouldNotReportError()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            using System;
            class Sample
            {
                void Test(string test)
                {
                    ArgumentNullException.ThrowIfNull(0 == 1, nameof(test));
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ThrowIfNull_WithBooleanExpressionAndInvalidParamName_ShouldReportError()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            using System;
            class Sample
            {
                void Test(string test)
                {
                    ArgumentNullException.ThrowIfNull(0 == 1, {|#0:"invalid"|});
                }
            }
            """;
        test.ExpectedDiagnostics.Add(new DiagnosticResult("TRX0015", DiagnosticSeverity.Warning).WithLocation(0).WithMessage("'invalid' is not a valid parameter name"));

        return test.RunAsync();
    }

    [Fact]
    public Task ThrowIfNull_MemberAccess_OptionDisabled_ShouldReportError()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            using System;
            class Sample
            {
                void Test(Request request)
                {
                    ArgumentNullException.ThrowIfNull({|TRX0015:request.Definition|});
                }
            }
            class Request { public string? Definition { get; set; } }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ThrowIfNull_MemberAccess_OptionEnabled_ShouldNotReportError()
    {
        var test = new AnalyzerTest();
        test.TestState.SetConfiguration("TRX0015.consider_member_access_as_parameter", "true");
        test.TestCode = """
            using System;
            class Sample
            {
                void Test(Request request)
                {
                    ArgumentNullException.ThrowIfNull(request.Definition);
                }
            }
            class Request { public string? Definition { get; set; } }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ThrowIfNull_DeepMemberAccess_OptionEnabled_ShouldNotReportError()
    {
        var test = new AnalyzerTest();
        test.TestState.SetConfiguration("TRX0015.consider_member_access_as_parameter", "true");
        test.TestCode = """
            using System;
            class Sample
            {
                void Test(Request request)
                {
                    ArgumentNullException.ThrowIfNull(request.Inner.Definition);
                }
            }
            class Inner { public string? Definition { get; set; } }
            class Request { public Inner? Inner { get; set; } }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ThrowIfNull_MemberAccess_NonParameterRoot_OptionEnabled_ShouldReportError()
    {
        var test = new AnalyzerTest();
        test.TestState.SetConfiguration("TRX0015.consider_member_access_as_parameter", "true");
        test.TestCode = """
            using System;
            class Sample
            {
                void Test(string test)
                {
                    ArgumentNullException.ThrowIfNull({|TRX0015:Name.Length|});
                }

                public static string Name { get; } = "";
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ThrowIfNull_ExplicitDottedParamName_OptionEnabled_ShouldNotReportError()
    {
        var test = new AnalyzerTest();
        test.TestState.SetConfiguration("TRX0015.consider_member_access_as_parameter", "true");
        test.TestCode = """
            using System;
            class Sample
            {
                void Test(Request request)
                {
                    ArgumentNullException.ThrowIfNull(request.Definition, "request.Definition");
                }
            }
            class Request { public string? Definition { get; set; } }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ThrowIfNull_ExplicitDottedParamName_OptionDisabled_ShouldReportError()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            using System;
            class Sample
            {
                void Test(Request request)
                {
                    ArgumentNullException.ThrowIfNull(request.Definition, {|TRX0015:"request.Definition"|});
                }
            }
            class Request { public string? Definition { get; set; } }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ArgumentNullException_Constructor_DottedParamName_OptionEnabled_ShouldNotReportError()
    {
        var test = new AnalyzerTest();
        test.TestState.SetConfiguration("TRX0015.consider_member_access_as_parameter", "true");
        test.TestCode = """
            using System;
            class Sample
            {
                void Test(Request request)
                {
                    if (request.Definition is null)
                        throw new ArgumentNullException("request.Definition");
                }
            }
            class Request { public string? Definition { get; set; } }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ArgumentNullException_Constructor_DottedParamName_OptionDisabled_ShouldReportError()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            using System;
            class Sample
            {
                void Test(Request request)
                {
                    if (request.Definition is null)
                        throw new ArgumentNullException({|TRX0015:"request.Definition"|});
                }
            }
            class Request { public string? Definition { get; set; } }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task UseNameof_Property()
    {
        var test = new CodeFixTest();
        test.TestCode = """
            class Sample
            {
                string Prop
                {
                    get { throw null; }
                    set { throw new System.ArgumentNullException({|TRX0043:"value"|}); }
                }
            }
            """;
        test.FixedCode = """
            class Sample
            {
                string Prop
                {
                    get { throw null; }
                    set { throw new System.ArgumentNullException(nameof(value)); }
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task UseNameof_Method()
    {
        var test = new CodeFixTest();
        test.TestCode = """
            class Sample
            {
                string M(string arg0)
                {
                    throw new System.ArgumentNullException({|TRX0043:"arg0"|});
                }
            }
            """;
        test.FixedCode = """
            class Sample
            {
                string M(string arg0)
                {
                    throw new System.ArgumentNullException(nameof(arg0));
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task UseNameof_Operator()
    {
        var test = new CodeFixTest();
        test.TestCode = """
            class Sample
            {
                public static Sample operator +(Sample first, Sample second)
                {
                    throw new System.ArgumentNullException({|TRX0043:"first"|});
                }
            }
            """;
        test.FixedCode = """
            class Sample
            {
                public static Sample operator +(Sample first, Sample second)
                {
                    throw new System.ArgumentNullException(nameof(first));
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task UseNameof_ThrowIfNull()
    {
        var test = new CodeFixTest();
        test.TestCode = """
            using System;
            class Sample
            {
                void M(string arg0)
                {
                    ArgumentNullException.ThrowIfNull(arg0, {|TRX0043:"arg0"|});
                }
            }
            """;
        test.FixedCode = """
            using System;
            class Sample
            {
                void M(string arg0)
                {
                    ArgumentNullException.ThrowIfNull(arg0, nameof(arg0));
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task InvalidEnumArgumentException_ValidArgumentName_ShouldNotReportError()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            class Sample
            {
                void M(int arg0)
                {
                    throw new System.ComponentModel.InvalidEnumArgumentException(nameof(arg0), arg0, typeof(System.DayOfWeek));
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task InvalidEnumArgumentException_InvalidArgumentName_ShouldReportError()
    {
        var test = new AnalyzerTest();
        test.TestCode = """
            class Sample
            {
                void M(int arg0)
                {
                    throw new System.ComponentModel.InvalidEnumArgumentException({|#0:"invalid"|}, arg0, typeof(System.DayOfWeek));
                }
            }
            """;
        test.ExpectedDiagnostics.Add(new DiagnosticResult("TRX0015", DiagnosticSeverity.Warning).WithLocation(0).WithMessage("'invalid' is not a valid parameter name"));

        return test.RunAsync();
    }
}
