using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using DiagnosticResult = Microsoft.CodeAnalysis.Testing.DiagnosticResult;
using CodeFixTest = Trax.Analyzers.Tests.Harness.CSharpCodeFixTest<
    Trax.Analyzers.Rules.MethodsReturningAnAwaitableTypeMustHaveTheAsyncSuffixAnalyzer,
    Trax.Analyzers.Rules.MethodsReturningAnAwaitableTypeMustHaveTheAsyncSuffixFixer>;

namespace Trax.Analyzers.Tests.Rules;

public sealed class MethodsReturningAnAwaitableTypeMustHaveTheAsyncSuffixAnalyzerTests
{
    private static CodeFixTest CreateTest()
    {
        var test = new CodeFixTest();
        return test;
    }

    [Fact]
    public Task AsyncMethodWithSuffix()
    {
        var test = CreateTest();
        test.TestCode = """
                class TypeName
                {
                    System.Threading.Tasks.Task TestAsync() => throw null;
                }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task AsyncMethodWithoutSuffix()
    {
        var test = CreateTest();
        test.TestCode = """
            class TypeName
            {
                System.Threading.Tasks.Task {|TRX0137:Test|}() => throw null;
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task AsyncMethodWithoutSuffix_NonAwaitableTypeAttribute_TaskWrappedType_Diagnostic()
    {
        var test = CreateTest();
        test.TestState.AddTraxAnnotations();
        test.TestCode = """
            using System.Threading.Tasks;
            [assembly: Trax.Analyzers.Annotations.NonAwaitableTypeAttribute(typeof(Result))]

            class TypeName
            {
                Task<Result> {|TRX0137:Test|}() => throw null;
            }

            class Result { }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task AsyncMethodWithoutSuffix_NonAwaitableTypeAttribute_OpenGenericTaskWrappedType_Diagnostic()
    {
        var test = CreateTest();
        test.TestState.AddTraxAnnotations();
        test.TestCode = """
            using System.Threading.Tasks;
            [assembly: Trax.Analyzers.Annotations.NonAwaitableTypeAttribute(typeof(Result<>))]

            class TypeName
            {
                Task<Result<int>> {|TRX0137:Test|}() => throw null;
            }

            class Result<T> { }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task VoidMethodWithSuffix()
    {
        var test = CreateTest();
        test.TestCode = """
            class TypeName
            {
                void {|TRX0138:TestAsync|}() => throw null;
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task VoidMethodWithoutSuffix()
    {
        var test = CreateTest();
        test.TestCode = """
            class TypeName
            {
                void Test() => throw null;
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task VoidLocalFunctionWithSuffix()
    {
        var test = CreateTest();
        test.TestCode = """
            class TypeName
            {
                void Test()
                {
                    void {|TRX0138:FooAsync|}() => throw null;
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task VoidLocalFunctionWithoutSuffix()
    {
        var test = CreateTest();
        test.TestCode = """
            class TypeName
            {
                void Test()
                {
                    void Foo() => throw null;
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task AwaitableLocalFunctionWithoutSuffix()
    {
        var test = CreateTest();
        test.TestCode = """
            class TypeName
            {
                void Test()
                {
                    _ = Foo();
                    System.Threading.Tasks.Task {|TRX0137:Foo|}() => throw null;
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task AwaitableLocalFunctionWithSuffix()
    {
        var test = CreateTest();
        test.TestCode = """
            class TypeName
            {
                void Test()
                {
                    System.Threading.Tasks.Task FooAsync() => throw null;
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task TopLevelStatement()
    {
        var test = CreateTest();
        test.TestState.OutputKind = OutputKind.ConsoleApplication;
        test.TestCode = """
            await System.Threading.Tasks.Task.Yield();
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task EntryPoint()
    {
        var test = CreateTest();
        test.TestState.OutputKind = OutputKind.ConsoleApplication;
        test.TestCode = """
            static class Program
            {
                static async System.Threading.Tasks.Task Main()
                {
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task IAsyncEnumerableWithoutSuffix()
    {
        var test = CreateTest();
        test.TestCode = """
            class TypeName
            {
                System.Collections.Generic.IAsyncEnumerable<int> {|#0:Foo|}() => throw null;
            }
            """;
        test.ExpectedDiagnostics.Add(new DiagnosticResult("TRX0156", DiagnosticSeverity.Warning).WithLocation(0).WithMessage("Method returning IAsyncEnumerable<T> must use the 'Async' suffix"));

        return test.RunAsync();
    }

    [Fact]
    public Task IAsyncEnumerableWithoutSuffix_CodeFix_AddsAsyncSuffix()
    {
        var test = CreateTest();
        // TRX0156 and TRX0157 contradict each other, a project enables one of them, not both,
        // so applying this fix reveals the other rule and the test asserts a single application
        test.FixedState.MarkupHandling = MarkupMode.Allow;
        test.CodeFixTestBehaviors |= CodeFixTestBehaviors.FixOne;
        test.TestCode = """
            class TypeName
            {
                System.Collections.Generic.IAsyncEnumerable<int> {|TRX0156:Foo|}() => throw null;
                void Caller() { _ = Foo(); }
            }
            """;
        test.FixedCode = """
            class TypeName
            {
                System.Collections.Generic.IAsyncEnumerable<int> {|TRX0157:FooAsync|}() => throw null;
                void Caller() { _ = FooAsync(); }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task IAsyncEnumerableWithSuffix()
    {
        var test = CreateTest();
        test.TestCode = """
            class TypeName
            {
                System.Collections.Generic.IAsyncEnumerable<int> {|#0:FooAsync|}() => throw null;
            }
            """;
        test.ExpectedDiagnostics.Add(new DiagnosticResult("TRX0157", DiagnosticSeverity.Warning).WithLocation(0).WithMessage("Method returning IAsyncEnumerable<T> must not use the 'Async' suffix"));

        return test.RunAsync();
    }

    [Fact]
    public Task IAsyncEnumerableWithSuffix_CodeFix_RemovesAsyncSuffix()
    {
        var test = CreateTest();
        // TRX0156 and TRX0157 contradict each other, a project enables one of them, not both,
        // so applying this fix reveals the other rule and the test asserts a single application
        test.FixedState.MarkupHandling = MarkupMode.Allow;
        test.CodeFixTestBehaviors |= CodeFixTestBehaviors.FixOne;
        test.TestCode = """
            class TypeName
            {
                System.Collections.Generic.IAsyncEnumerable<int> {|TRX0157:FooAsync|}() => throw null;
                void Caller() { _ = FooAsync(); }
            }
            """;
        test.FixedCode = """
            class TypeName
            {
                System.Collections.Generic.IAsyncEnumerable<int> {|TRX0156:Foo|}() => throw null;
                void Caller() { _ = Foo(); }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task IgnoreTestMethods()
    {
        var test = CreateTest();
        test.ReferenceAssemblies = test.ReferenceAssemblies.AddXunitV3();
        test.TestCode = """
            class TypeName
            {
                [Xunit.Fact]
                System.Threading.Tasks.Task Foo() => throw null;
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task AsyncMethodWithoutSuffix_CodeFix_AddsAsyncSuffix()
    {
        var test = CreateTest();
        test.TestCode = """
            class TypeName
            {
                System.Threading.Tasks.Task {|TRX0137:Test|}() => throw null;
                void Caller() { _ = Test(); }
            }
            """;
        test.FixedCode = """
            class TypeName
            {
                System.Threading.Tasks.Task TestAsync() => throw null;
                void Caller() { _ = TestAsync(); }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task MethodNotReturningAwaitableTypeWithSuffix_CodeFix_RemovesAsyncSuffix()
    {
        var test = CreateTest();
        test.TestCode = """
            class TypeName
            {
                void {|TRX0138:TestAsync|}() => throw null;
                void Caller() { TestAsync(); }
            }
            """;
        test.FixedCode = """
            class TypeName
            {
                void Test() => throw null;
                void Caller() { Test(); }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task VoidLocalFunctionWithSuffix_CodeFix_RemovesAsyncSuffix()
    {
        var test = CreateTest();
        test.TestCode = """
            class TypeName
            {
                void Test()
                {
                    void {|TRX0138:FooAsync|}() => throw null;
                    FooAsync();
                }
            }
            """;
        test.FixedCode = """
            class TypeName
            {
                void Test()
                {
                    void Foo() => throw null;
                    Foo();
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task IgnoreTestMethods_ExcludeTestMethodsTrue()
    {
        var test = CreateTest();
        test.TestState.SetConfiguration("TRX0137.exclude_test_methods", "true");
        test.ReferenceAssemblies = test.ReferenceAssemblies.AddXunitV3();
        test.TestCode = """
            class TypeName
            {
                [Xunit.Fact]
                System.Threading.Tasks.Task Foo() => throw null;
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task IgnoreTestMethods_ExcludeTestMethodsFalse()
    {
        var test = CreateTest();
        test.TestState.SetConfiguration("TRX0137.exclude_test_methods", "false");
        test.ReferenceAssemblies = test.ReferenceAssemblies.AddXunitV3();
        test.TestCode = """
            class TypeName
            {
                [Xunit.Fact]
                System.Threading.Tasks.Task {|TRX0137:Foo|}() => throw null;
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ConfigureAwait_IsIgnored()
    {
        var test = CreateTest();
        test.TestCode = """
            using System.Runtime.CompilerServices;
            using System.Threading.Tasks;
            class TypeName
            {
                ConfiguredTaskAwaitable ConfigureAwait(bool continueOnCapturedContext) => Task.CompletedTask.ConfigureAwait(continueOnCapturedContext);
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task GetAwaiter_IsIgnored()
    {
        var test = CreateTest();
        test.TestCode = """
            using System.Runtime.CompilerServices;
            using System.Threading.Tasks;
            class TypeName
            {
                TaskAwaiter GetAwaiter() => Task.CompletedTask.GetAwaiter();
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task WithCancellation_IsIgnored()
    {
        var test = CreateTest();
        test.TestCode = """
            using System.Collections.Generic;
            using System.Threading;
            class TypeName
            {
                IAsyncEnumerable<int> WithCancellation(CancellationToken cancellationToken) => throw null;
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task PropertyReturningTask_IsIgnored()
    {
        var test = CreateTest();
        test.TestCode = """
            using System.Threading.Tasks;
            class TypeName
            {
                Task Task => Task.CompletedTask;
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task PropertyReturningTask_ExcludePropertyAccessorsTrue()
    {
        var test = CreateTest();
        test.TestState.SetConfiguration("TRX0137.exclude_property_accessors", "true");
        test.TestCode = """
            using System.Threading.Tasks;
            class TypeName
            {
                Task Task => Task.CompletedTask;
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task PropertyReturningTask_ExcludePropertyAccessorsFalse_Diagnostic()
    {
        var test = CreateTest();
        test.TestState.SetConfiguration("TRX0137.exclude_property_accessors", "false");
        test.TestCode = """
            using System.Threading.Tasks;
            class TypeName
            {
                Task Task
                {
                    {|TRX0137:get|} => System.Threading.Tasks.Task.CompletedTask;
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ConversionOperatorReturningTask_IsIgnored()
    {
        var test = CreateTest();
        test.TestCode = """
            using System.Threading.Tasks;
            class Test
            {
                public static implicit operator Task(Test t) => Task.CompletedTask;
                public static explicit operator ValueTask(Test t) => default;
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task OperatorReturningTask_IsIgnored()
    {
        var test = CreateTest();
        test.TestCode = """
            using System.Threading.Tasks;
            class Test
            {
                public static Task operator +(Test a, Test b) => Task.CompletedTask;
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    [Trait("Issue", "https://github.com/meziantou/Meziantou.Analyzer/issues/1648")]
    public Task NotAwaitable_RenameConflictsWithExistingMethod_NoCodeFix()
    {
        const string Source = """
            class Test
            {
                public void Write() => throw null;
                void {|TRX0138:WriteAsync|}() => throw null;
            }
            """;
        var test = CreateTest();
        test.TestCode = Source;
        test.FixedCode = Source;

        return test.RunAsync();
    }

    [Fact]
    public Task NotAwaitable_RenameToOverloadWithDifferentParameters()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test
            {
                public void Write(int value) => throw null;
                void {|TRX0138:WriteAsync|}() => throw null;
            }
            """;
        test.FixedCode = """
            class Test
            {
                public void Write(int value) => throw null;
                void Write() => throw null;
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task NotAwaitable_RenameConflictsWithGenericMethod_NoCodeFix()
    {
        const string Source = """
            class Test
            {
                public void Write<T>(T value) => throw null;
                void {|TRX0138:WriteAsync|}<U>(U value) => throw null;
            }
            """;
        var test = CreateTest();
        test.TestCode = Source;
        test.FixedCode = Source;

        return test.RunAsync();
    }

    [Fact]
    public Task NotAwaitable_RenameConflictsWithProperty_NoCodeFix()
    {
        const string Source = """
            class Test
            {
                public int Write => 0;
                void {|TRX0138:WriteAsync|}() => throw null;
            }
            """;
        var test = CreateTest();
        test.TestCode = Source;
        test.FixedCode = Source;

        return test.RunAsync();
    }

    [Fact]
    public Task NotAwaitable_LocalFunction_RenameConflictsWithLocal_NoCodeFix()
    {
        const string Source = """
            class Test
            {
                void A()
                {
                    var Foo = 0;
                    _ = Foo;
                    void {|TRX0138:FooAsync|}() => throw null;
                }
            }
            """;
        var test = CreateTest();
        test.TestCode = Source;
        test.FixedCode = Source;

        return test.RunAsync();
    }

    [Fact]
    [Trait("Issue", "https://github.com/meziantou/Meziantou.Analyzer/issues/1678")]
    public Task NotAwaitable_MethodNamedAsync_NoCodeFix()
    {
        const string Source = """
            class Test
            {
                void {|TRX0138:Async|}() => throw null;
            }
            """;
        var test = CreateTest();
        test.TestCode = Source;
        test.FixedCode = Source;

        return test.RunAsync();
    }

    [Fact]
    [Trait("Issue", "https://github.com/meziantou/Meziantou.Analyzer/issues/1684")]
    public Task InvalidCode_LocalFunctionDeclarationDoesNotCompile_NoCodeFix()
    {
        const string Source = """
            using System.Threading.Tasks static Task {|TRX0137:Main|}(){Task<>{|TRX0137:Inner|}(null Inner
            """;
        var test = CreateTest();
        // The code does not compile, so the compiler diagnostics cannot be verified
        test.CompilerDiagnostics = CompilerDiagnostics.None;
        test.TestState.OutputKind = OutputKind.ConsoleApplication;
        test.TestCode = Source;
        test.FixedCode = Source;

        return test.RunAsync();
    }

    [Fact]
    [Trait("Issue", "https://github.com/meziantou/Meziantou.Analyzer/issues/1690")]
    public Task InvalidCode_MethodDeclarationDoesNotCompile_NoCodeFix()
    {
        const string Source = """
            t A{{
            #if A
            #else
            T public k {|#0:AAsync|}<
            """;
        var test = CreateTest();
        // The code does not compile, so the compiler diagnostics cannot be verified
        test.CompilerDiagnostics = CompilerDiagnostics.None;
        test.TestState.OutputKind = OutputKind.ConsoleApplication;
        test.TestCode = Source;
        test.FixedCode = Source;
#if ROSLYN_4_14_OR_GREATER
        // The older versions of the parser do not declare a method for this invalid code
        test.TestState.ExpectedDiagnostics.Add(new DiagnosticResult("TRX0138", DiagnosticSeverity.Warning).WithLocation(0));
        test.FixedState.ExpectedDiagnostics.Add(new DiagnosticResult("TRX0138", DiagnosticSeverity.Warning).WithLocation(0));
#endif

        return test.RunAsync();
    }
}
