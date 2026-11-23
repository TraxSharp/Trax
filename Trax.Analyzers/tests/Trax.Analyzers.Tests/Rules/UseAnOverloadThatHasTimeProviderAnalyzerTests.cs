using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using CodeFixTest = Trax.Analyzers.Tests.Harness.CSharpCodeFixTest<
    Trax.Analyzers.Rules.UseAnOverloadThatHasTimeProviderAnalyzer,
    Trax.Analyzers.Rules.UseAnOverloadThatHasTimeProviderFixer>;

namespace Trax.Analyzers.Tests.Rules;

public sealed class UseAnOverloadThatHasTimeProviderAnalyzerTests
{
    private static CodeFixTest CreateTest() => new();

    [Fact]
    public Task NoReport_ConsoleWriteLine()
    {
        var test = CreateTest();
        test.TestState.OutputKind = OutputKind.ConsoleApplication;
        test.TestCode = """
            System.Console.WriteLine("test");
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task NoReport_TimeSpanFromSeconds()
    {
        var test = CreateTest();
        test.TestState.OutputKind = OutputKind.ConsoleApplication;
        test.TestCode = """
            _ = System.TimeSpan.FromSeconds(1);
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task NotAvailable()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test
            {
                void A()
                {
                    {|TRX0167:System.Threading.Tasks.Task.Delay(System.TimeSpan.Zero)|};
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task NoReport_WrongOverload()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test
            {
                public void A()
                {
                    B();
                }

                void B() { }
                void B(int a) { }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task WhenAvailable_Parameter()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test
            {
                void A(System.TimeProvider foo)
                {
                    {|TRX0166:System.Threading.Tasks.Task.Delay(System.TimeSpan.Zero)|};
                }
            }
            """;
        test.FixedCode = """
            class Test
            {
                void A(System.TimeProvider foo)
                {
                    System.Threading.Tasks.Task.Delay(System.TimeSpan.Zero, foo);
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task WhenAvailable_NestedProp()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test
            {
                void A(Sample foo)
                {
                    {|TRX0166:System.Threading.Tasks.Task.Delay(System.TimeSpan.Zero)|};
                }

                class Sample { public System.TimeProvider A {get;} }
            }
            """;
        test.FixedCode = """
            class Test
            {
                void A(Sample foo)
                {
                    System.Threading.Tasks.Task.Delay(System.TimeSpan.Zero, foo.A);
                }

                class Sample { public System.TimeProvider A {get;} }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task WhenAvailable_DerivedTimeProviderParameter()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test
            {
                void A(MyTimeProvider foo)
                {
                    {|TRX0166:System.Threading.Tasks.Task.Delay(System.TimeSpan.Zero)|};
                }

                class MyTimeProvider : System.TimeProvider { }
            }
            """;
        test.FixedCode = """
            class Test
            {
                void A(MyTimeProvider foo)
                {
                    System.Threading.Tasks.Task.Delay(System.TimeSpan.Zero, foo);
                }

                class MyTimeProvider : System.TimeProvider { }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task WhenAvailable_NestedPropOfDerivedType()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test
            {
                void A(Sample foo)
                {
                    {|TRX0166:System.Threading.Tasks.Task.Delay(System.TimeSpan.Zero)|};
                }

                class Sample { public MyTimeProvider A {get;} }
                class MyTimeProvider : System.TimeProvider { }
            }
            """;
        test.FixedCode = """
            class Test
            {
                void A(Sample foo)
                {
                    System.Threading.Tasks.Task.Delay(System.TimeSpan.Zero, foo.A);
                }

                class Sample { public MyTimeProvider A {get;} }
                class MyTimeProvider : System.TimeProvider { }
            }
            """;

        return test.RunAsync();
    }

    [Theory]
    [InlineData("public static System.TimeProvider Shared { get; }")]
    [InlineData("public static System.TimeProvider Shared;")]
    [InlineData("public System.TimeProvider Provider { set { } }")]
    [InlineData("public System.TimeProvider Provider { private get; set; }")]
    [InlineData("public System.TimeProvider this[int index] => null;")]
    public Task NotAvailable_NonUsableMember(string member)
    {
        var test = CreateTest();
        test.TestCode = $$"""
            class Test
            {
                void A(Sample foo)
                {
                    {|TRX0167:System.Threading.Tasks.Task.Delay(System.TimeSpan.Zero)|};
                }

                class Sample { {{member}} }
            }
            """;

        return test.RunAsync();
    }

    [Theory]
    [InlineData("static Task P => {|TRX0167:Task.Delay(TimeSpan.Zero)|};")]
    [InlineData("static Task P { get { return {|TRX0167:Task.Delay(TimeSpan.Zero)|}; } }")]
    [InlineData("static Task F = {|TRX0167:Task.Delay(TimeSpan.Zero)|};")]
    [InlineData("Task F = {|TRX0167:Task.Delay(TimeSpan.Zero)|};")]
    [InlineData("Task P { get; } = {|TRX0167:Task.Delay(TimeSpan.Zero)|};")]
    [InlineData("static Test() { {|TRX0167:Task.Delay(TimeSpan.Zero)|}; }")]
    [InlineData("Test() : this({|TRX0167:Task.Delay(TimeSpan.Zero)|}) { } Test(Task task) { }")]
    [InlineData("static event Action E { add { {|TRX0167:Task.Delay(TimeSpan.Zero)|}; } remove { } }")]
    [InlineData("static int P { get { void Local() => {|TRX0167:Task.Delay(TimeSpan.Zero)|}; return 0; } }")]
    [InlineData("class Nested { void A() { {|TRX0167:Task.Delay(TimeSpan.Zero)|}; } }")]
    public Task NotAvailable_InstanceMemberInStaticContext(string member)
    {
        var test = CreateTest();
        test.TestCode = $$"""
            using System;
            using System.Threading.Tasks;

            class Test
            {
                private readonly TimeProvider _timeProvider = TimeProvider.System;

                {{member}}
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task WhenAvailable_StaticOperator_DoesNotReportInstanceMembers()
    {
        var test = CreateTest();
        test.TestCode = """
            using System;
            using System.Threading.Tasks;

            class Test
            {
                private readonly TimeProvider _timeProvider = TimeProvider.System;

                public static Test operator +(Test a, Test b)
                {
                    {|#0:Task.Delay(TimeSpan.Zero)|};
                    return a;
                }
            }
            """;
        test.ExpectedDiagnostics.Add(new DiagnosticResult("TRX0166", DiagnosticSeverity.Info).WithLocation(0).WithMessage("Use an overload with a TimeProvider, available time providers: a._timeProvider, b._timeProvider"));

        return test.RunAsync();
    }

    [Theory]
    [InlineData("Task F = {|TRX0166:Task.Delay(TimeSpan.Zero)|};", "Task F = Task.Delay(TimeSpan.Zero, timeProvider);")]
    [InlineData("Task P { get; } = {|TRX0166:Task.Delay(TimeSpan.Zero)|};", "Task P { get; } = Task.Delay(TimeSpan.Zero, timeProvider);")]
    [InlineData("Task P => {|TRX0166:Task.Delay(TimeSpan.Zero)|};", "Task P => Task.Delay(TimeSpan.Zero, timeProvider);")]
    public Task WhenAvailable_PrimaryConstructorParameter(string member, string fixedMember)
    {
        var test = CreateTest();
        test.TestCode = $$"""
            using System;
            using System.Threading.Tasks;

            class Test(TimeProvider timeProvider)
            {
                {{member}}
            }
            """;
        test.FixedCode = $$"""
            using System;
            using System.Threading.Tasks;

            class Test(TimeProvider timeProvider)
            {
                {{fixedMember}}
            }
            """;

        return test.RunAsync();
    }

    [Theory]
    [InlineData("static Task F = {|TRX0167:Task.Delay(TimeSpan.Zero)|};")]
    [InlineData("static void A() { {|TRX0167:Task.Delay(TimeSpan.Zero)|}; }")]
    [InlineData("Test() : this({|TRX0167:Task.Delay(TimeSpan.Zero)|}, null) { }")]
    public Task NotAvailable_PrimaryConstructorParameter(string member)
    {
        var test = CreateTest();
        test.TestCode = $$"""
            using System;
            using System.Threading.Tasks;

            class Test(Task task, TimeProvider timeProvider)
            {
                Task Task => task;
                TimeProvider TimeProvider => timeProvider;

                {{member}}
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task WhenAvailable_NestedPropOfNestedProp()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test
            {
                void A(Sample foo)
                {
                    {|TRX0166:System.Threading.Tasks.Task.Delay(System.TimeSpan.Zero)|};
                }

                class Sample { public Nested B {get;} }
                class Nested { public System.TimeProvider A {get;} }
            }
            """;
        test.FixedCode = """
            class Test
            {
                void A(Sample foo)
                {
                    System.Threading.Tasks.Task.Delay(System.TimeSpan.Zero, foo.B.A);
                }

                class Sample { public Nested B {get;} }
                class Nested { public System.TimeProvider A {get;} }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task OptionalParameter_WhenAvailable()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test
            {
                static void Delay(System.TimeProvider timeProvider = null)
                {
                }

                void A(System.TimeProvider foo)
                {
                    {|TRX0166:Delay()|};
                }
            }
            """;
        test.FixedCode = """
            class Test
            {
                static void Delay(System.TimeProvider timeProvider = null)
                {
                }

                void A(System.TimeProvider foo)
                {
                    Delay(foo);
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task OptionalParameter_WithOptionalParameterBeforeTimeProvider()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test
            {
                static void Delay(bool dummy = false, System.TimeProvider timeProvider = null)
                {
                }

                void A(System.TimeProvider foo)
                {
                    {|TRX0166:Delay()|};
                }
            }
            """;
        test.FixedCode = """
            class Test
            {
                static void Delay(bool dummy = false, System.TimeProvider timeProvider = null)
                {
                }

                void A(System.TimeProvider foo)
                {
                    Delay(timeProvider: foo);
                }
            }
            """;

        return test.RunAsync();
    }
    [Fact]
    public Task NamedArguments_OutOfOrder()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test
            {
                System.Threading.Tasks.Task A(System.TimeProvider foo, System.Threading.CancellationToken token)
                    => {|TRX0166:System.Threading.Tasks.Task.Delay(cancellationToken: token, delay: System.TimeSpan.Zero)|};
            }
            """;
        test.FixedCode = """
            class Test
            {
                System.Threading.Tasks.Task A(System.TimeProvider foo, System.Threading.CancellationToken token)
                    => System.Threading.Tasks.Task.Delay(cancellationToken: token, delay: System.TimeSpan.Zero, timeProvider: foo);
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task NamedArguments_InOrder()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test
            {
                System.Threading.Tasks.Task A(System.TimeProvider foo, System.Threading.CancellationToken token)
                    => {|TRX0166:System.Threading.Tasks.Task.Delay(delay: System.TimeSpan.Zero, cancellationToken: token)|};
            }
            """;
        test.FixedCode = """
            class Test
            {
                System.Threading.Tasks.Task A(System.TimeProvider foo, System.Threading.CancellationToken token)
                    => System.Threading.Tasks.Task.Delay(delay: System.TimeSpan.Zero, cancellationToken: token, timeProvider: foo);
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task NamedArgumentAfterTimeProviderParameter()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test
            {
                System.Threading.Tasks.Task A(System.TimeProvider foo, System.Threading.CancellationToken token)
                    => {|TRX0166:System.Threading.Tasks.Task.Delay(System.TimeSpan.Zero, cancellationToken: token)|};
            }
            """;
        test.FixedCode = """
            class Test
            {
                System.Threading.Tasks.Task A(System.TimeProvider foo, System.Threading.CancellationToken token)
                    => System.Threading.Tasks.Task.Delay(System.TimeSpan.Zero, foo, cancellationToken: token);
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task PositionalArgumentAfterTimeProviderParameter()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test
            {
                System.Threading.Tasks.Task A(System.TimeProvider foo, System.Threading.CancellationToken token)
                    => {|TRX0166:System.Threading.Tasks.Task.Delay(System.TimeSpan.Zero, token)|};
            }
            """;
        test.FixedCode = """
            class Test
            {
                System.Threading.Tasks.Task A(System.TimeProvider foo, System.Threading.CancellationToken token)
                    => System.Threading.Tasks.Task.Delay(System.TimeSpan.Zero, foo, token);
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task OptionalParameter_WithNamedArgumentBeforeTimeProvider()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test
            {
                static void Delay(int a = 0, int b = 0, System.TimeProvider timeProvider = null)
                {
                }

                void A(System.TimeProvider foo)
                {
                    {|TRX0166:Delay(b: 1)|};
                }
            }
            """;
        test.FixedCode = """
            class Test
            {
                static void Delay(int a = 0, int b = 0, System.TimeProvider timeProvider = null)
                {
                }

                void A(System.TimeProvider foo)
                {
                    Delay(b: 1, timeProvider: foo);
                }
            }
            """;

        return test.RunAsync();
    }
    [Fact]
    public Task ExtensionMethod()
    {
        var test = CreateTest();
        test.TestCode = """
            static class Test
            {
                static void A(this string str, int a) { }
                static void A(this string str, int a, System.TimeProvider timeProvider) { }

                static void B(System.TimeProvider foo) => {|TRX0166:"".A(0)|};
            }
            """;
        test.FixedCode = """
            static class Test
            {
                static void A(this string str, int a) { }
                static void A(this string str, int a, System.TimeProvider timeProvider) { }

                static void B(System.TimeProvider foo) => "".A(0, timeProvider: foo);
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ParamsParameter()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test
            {
                static void Delay(params int[] values) { }
                static void Delay(System.TimeProvider timeProvider, params int[] values) { }

                void A(System.TimeProvider foo)
                {
                    {|TRX0166:Delay(1, 2)|};
                }
            }
            """;
        test.FixedCode = """
            class Test
            {
                static void Delay(params int[] values) { }
                static void Delay(System.TimeProvider timeProvider, params int[] values) { }

                void A(System.TimeProvider foo)
                {
                    Delay(foo, 1, 2);
                }
            }
            """;

        return test.RunAsync();
    }
    [Fact]
    public Task NoFix_WhenTheProviderCannotBeAddedWithoutReorderingTheArguments()
    {
        var test = CreateTest();
        test.TestCode = """
            static class Test
            {
                static void A(this string str, int a) { }
                static void A(this string str, System.TimeProvider timeProvider, int a) { }

                static void B(System.TimeProvider foo) => {|TRX0166:"".A(0)|};
            }
            """;
        test.FixedCode = """
            static class Test
            {
                static void A(this string str, int a) { }
                static void A(this string str, System.TimeProvider timeProvider, int a) { }

                static void B(System.TimeProvider foo) => {|TRX0166:"".A(0)|};
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ReportsTheAvailableTimeProvidersInTheMessage()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test
            {
                void A(System.TimeProvider timeProvider)
                {
                    {|#0:Delay()|};
                }

                static void Delay() { }
                static void Delay(System.TimeProvider timeProvider) { }
            }
            """;
        test.FixedCode = """
            class Test
            {
                void A(System.TimeProvider timeProvider)
                {
                    Delay(timeProvider);
                }

                static void Delay() { }
                static void Delay(System.TimeProvider timeProvider) { }
            }
            """;
        test.ExpectedDiagnostics.Add(new DiagnosticResult("TRX0166", DiagnosticSeverity.Info).WithLocation(0).WithMessage("Use an overload with a TimeProvider, available time providers: timeProvider"));

        return test.RunAsync();
    }

    [Fact]
    public Task ExtensionMethod_NamespaceNotImported_DisabledByDefault()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test
            {
                void A(Sample sample, System.TimeProvider timeProvider)
                {
                    sample.Run();
                }
            }

            public class Sample
            {
                public void Run() => throw null;
            }

            namespace Ext
            {
                public static class SampleExtensions
                {
                    public static void Run(this Sample sample, System.TimeProvider timeProvider) => throw null;
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ExtensionMethod_NamespaceNotImported_EnabledForAnotherRule()
    {
        var test = CreateTest();
        test.TestState.SetConfiguration("TRX0167.include_extension_methods_from_not_imported_namespaces", "true");
        test.TestCode = """
            class Test
            {
                void A(Sample sample, System.TimeProvider timeProvider)
                {
                    sample.Run();
                }

                void B(Sample sample)
                {
                    {|TRX0167:sample.Run()|};
                }
            }

            public class Sample
            {
                public void Run() => throw null;
            }

            namespace Ext
            {
                public static class SampleExtensions
                {
                    public static void Run(this Sample sample, System.TimeProvider timeProvider) => throw null;
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ExtensionMethod_NamespaceNotImported_AddUsingDirective()
    {
        var test = CreateTest();
        test.TestState.SetConfiguration("TRX0166.include_extension_methods_from_not_imported_namespaces", "true");
        test.TestCode = """
            class Test
            {
                void A(Sample sample, System.TimeProvider timeProvider)
                {
                    {|TRX0166:sample.Run()|};
                }
            }

            public class Sample
            {
                public void Run() => throw null;
            }

            namespace Ext
            {
                public static class SampleExtensions
                {
                    public static void Run(this Sample sample, System.TimeProvider timeProvider) => throw null;
                }
            }
            """;
        test.FixedCode = """
            using Ext;

            class Test
            {
                void A(Sample sample, System.TimeProvider timeProvider)
                {
                    sample.Run(timeProvider);
                }
            }

            public class Sample
            {
                public void Run() => throw null;
            }

            namespace Ext
            {
                public static class SampleExtensions
                {
                    public static void Run(this Sample sample, System.TimeProvider timeProvider) => throw null;
                }
            }
            """;

        return test.RunAsync();
    }
}
