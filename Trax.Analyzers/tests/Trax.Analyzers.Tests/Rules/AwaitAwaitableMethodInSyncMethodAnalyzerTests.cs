using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using AnalyzerTest = Trax.Analyzers.Tests.Harness.CSharpAnalyzerTest<
    Trax.Analyzers.Rules.AwaitAwaitableMethodInSyncMethodAnalyzer>;

namespace Trax.Analyzers.Tests.Rules;

public sealed class AwaitAwaitableMethodInSyncMethodAnalyzerTests
{
    private static AnalyzerTest CreateTest() => new();

    [Fact]
    public Task NoReport_NonAwaitedTaskInAsyncMethod()
    {
        var test = CreateTest();
        test.TestCode = """
            using System.Threading.Tasks;
            class Test
            {
                async Task A()
                {
                    Task.Delay(0);
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task NoReport_AwaitedTaskInAsyncMethod()
    {
        var test = CreateTest();
        test.TestCode = """
            using System.Threading.Tasks;
            class Test
            {
                async Task A()
                {
                    await Task.Delay(0);
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task NoReport_TaskInAsyncLocalFunction()
    {
        var test = CreateTest();
        test.TestCode = """
            using System.Threading.Tasks;
            class Test
            {
                void A()
                {
                    B();
                    async void B()
                    {
                        Task.Delay(0);
                    }
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task NoReport_TaskInAsyncLambdaFunction()
    {
        var test = CreateTest();
        test.TestCode = """
            using System.Threading.Tasks;
            class Test
            {
                void A()
                {
                    _ = new System.Action(async () =>
                    {
                        Task.Delay(0);
                    });
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task NoReport_Discard()
    {
        var test = CreateTest();
        test.TestCode = """
            using System.Threading.Tasks;
            class Test
            {
                void A()
                {
                    _ = Task.Delay(0);
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task Report_Discard_WhenConfigured()
    {
        var test = CreateTest();
        test.TestState.SetConfiguration("TRX0134.report_discarded", "true");
        test.TestCode = """
            using System.Threading.Tasks;
            class Test
            {
                void A()
                {
                    _ = {|TRX0134:Task.Delay(0)|};
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task Report_Discard_InConstructor_WhenConfigured()
    {
        var test = CreateTest();
        test.TestState.SetConfiguration("TRX0134.report_discarded", "true");
        test.TestCode = """
            using System.Threading.Tasks;
            class Test
            {
                Test()
                {
                    _ = {|TRX0134:StartAsync()|};
                }

                Task StartAsync() => Task.Delay(0);
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task NoReport_TopLevelStatement()
    {
        var test = CreateTest();
        test.TestState.OutputKind = OutputKind.ConsoleApplication;
        test.TestCode = """
            using System.Threading.Tasks;

            Task.Delay(0);
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task NoReport_FireAndForget()
    {
        var test = CreateTest();
        test.TestCode = """
            using System.Threading.Tasks;
            class Test
            {
                void A()
                {
                    Task.Delay(0).FireAndForget();
                }
            }

            static class Extensions
            {
                public static void FireAndForget(this Task task) => throw null;
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task Report_TaskInSyncMethodReturningTask()
    {
        var test = CreateTest();
        test.TestCode = """
            using System.Threading.Tasks;
            class Test
            {
                Task A()
                {
                    {|TRX0134:Task.Delay(0)|};
                    return Task.CompletedTask;
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task Report_NonAwaitableTypeAttribute_TaskWrappedType_InSyncMethod()
    {
        var test = CreateTest();
        test.TestState.AddTraxAnnotations();
        test.TestCode = """
            using System.Threading.Tasks;
            [assembly: Trax.Analyzers.Annotations.NonAwaitableTypeAttribute(typeof(Result))]

            class Test
            {
                void A()
                {
                    {|TRX0134:B()|};
                }

                Task<Result> B() => throw null;
            }

            class Result { }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task Report_NonAwaitableTypeAttribute_OpenGenericTaskWrappedType_InSyncMethod()
    {
        var test = CreateTest();
        test.TestState.AddTraxAnnotations();
        test.TestCode = """
            using System.Threading.Tasks;
            [assembly: Trax.Analyzers.Annotations.NonAwaitableTypeAttribute(typeof(Result<>))]

            class Test
            {
                void A()
                {
                    {|TRX0134:B()|};
                }

                Task<Result<int>> B() => throw null;
            }

            class Result<T> { }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task Report_TaskInSyncVoidMethod()
    {
        var test = CreateTest();
        test.TestCode = """
            using System.Threading.Tasks;
            class Test
            {
                void A()
                {
                    {|TRX0134:Task.Delay(0)|};
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task Report_CustomAwaitableInSyncMethod()
    {
        var test = CreateTest();
        test.TestCode = """
            using System.Threading.Tasks;
            class Test
            {
                void A()
                {
                    {|TRX0134:Task.Yield()|};
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task Report_TaskInLambda()
    {
        var test = CreateTest();
        test.TestCode = """
            using System.Threading.Tasks;
            class Test
            {
                void A()
                {
                    _ = new System.Action(() =>
                    {
                        {|TRX0134:Task.Delay(0)|};
                    });
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task Report_TaskInLambda_Arrow()
    {
        var test = CreateTest();
        test.TestCode = """
            using System.Threading.Tasks;
            class Test
            {
                void A()
                {
                    _ = new System.Action(() => {|TRX0134:Task.Delay(0)|});
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task Report_TaskInDelegate()
    {
        var test = CreateTest();
        test.TestCode = """
            using System.Threading.Tasks;
            class Test
            {
                void A()
                {
                    _ = new System.Action(delegate
                    {
                        {|TRX0134:Task.Delay(0)|};
                    });
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task Report_TaskInDelegate_Parentheses()
    {
        var test = CreateTest();
        test.TestCode = """
            using System.Threading.Tasks;
            class Test
            {
                void A()
                {
                    _ = new System.Action(delegate()
                    {
                        {|TRX0134:Task.Delay(0)|};
                    });
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task Report_TaskInGetter()
    {
        var test = CreateTest();
        test.TestCode = """
            using System.Threading.Tasks;
            class Test
            {
                int A
                {
                    get
                    {
                        {|TRX0134:Task.Delay(0)|};
                        return 0;
                    }
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task Report_TaskInLocalFunction()
    {
        var test = CreateTest();
        test.TestCode = """
            using System.Threading.Tasks;
            class Test
            {
                void A()
                {
                    B();
                    void B()
                    {
                        {|TRX0134:Task.Delay(0)|};
                    }
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task Report_TaskInLocalTaskReturningFunction()
    {
        var test = CreateTest();
        test.TestCode = """
            using System.Threading.Tasks;
            class Test
            {
                async Task A()
                {
                    await B();

                    Task B()
                    {
                        {|TRX0134:Task.Delay(0)|};
                        return Task.CompletedTask;
                    }
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task Report_TaskConfigureAwait()
    {
        var test = CreateTest();
        test.TestCode = """
            using System.Threading.Tasks;
            class Test
            {
                void A()
                {
                    {|TRX0134:Task.Delay(0).ConfigureAwait(false)|};
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task Report_ConditionalInvoke()
    {
        var test = CreateTest();
        test.TestCode = """
            using System.Threading.Tasks;
            class Test
            {
                Task ReturnTask() => throw null;

                void A(Test instance)
                {
                    instance?{|TRX0134:.ReturnTask()|};
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task DoNotReport_Discard_ConditionalInvoke()
    {
        var test = CreateTest();
        test.TestCode = """
            using System.Threading.Tasks;
            class Test
            {
                Task ReturnTask() => throw null;

                void A(Test instance)
                {
                    _ = instance?.ReturnTask();
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task Report_Discard_ConditionalInvoke_WhenConfigured()
    {
        var test = CreateTest();
        test.TestState.SetConfiguration("TRX0134.report_discarded", "true");
        test.TestCode = """
            using System.Threading.Tasks;
            class Test
            {
                Task ReturnTask() => throw null;

                void A(Test instance)
                {
                    _ = instance?{|TRX0134:.ReturnTask()|};
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task Report_TaskInLambda_Arrow_InAsyncMethod()
    {
        var test = CreateTest();
        test.TestCode = """
            using System.Threading.Tasks;
            class Test
            {
                async Task A()
                {
                    _ = new System.Action(() => {|TRX0134:Task.Delay(0)|});
                    await Task.Yield();
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task NoReport_TaskInAsyncLambda_Arrow()
    {
        var test = CreateTest();
        test.TestCode = """
            using System.Threading.Tasks;
            class Test
            {
                void A()
                {
                    _ = new System.Func<Task>(async () => Task.Delay(0));
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task Report_TaskInLambdaInFieldInitializer()
    {
        var test = CreateTest();
        test.TestCode = """
            using System.Threading.Tasks;
            class Test
            {
                System.Action _a = () => {|TRX0134:Task.Delay(0)|};
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task Report_ExpressionBodiedMethod()
    {
        var test = CreateTest();
        test.TestCode = """
            using System.Threading.Tasks;
            class Test
            {
                void A() => {|TRX0134:Task.Delay(0)|};
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task NoReport_AsyncExpressionBodiedMethod()
    {
        var test = CreateTest();
        test.TestCode = """
            using System.Threading.Tasks;
            class Test
            {
                async Task A() => Task.Delay(0);
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task NoReport_NonAwaitableType()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test
            {
                void A(System.Text.StringBuilder sb)
                {
                    sb.Append('a');
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task Report_InheritedGetAwaiterMethod()
    {
        var test = CreateTest();
        test.TestCode = """
            using System.Runtime.CompilerServices;
            class Test
            {
                void A()
                {
                    {|TRX0134:B()|};
                }

                Derived B() => throw null;
            }

            class Base
            {
                public TaskAwaiter GetAwaiter() => throw null;
            }

            class Derived : Base { }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task Report_GetAwaiterExtensionMethod()
    {
        var test = CreateTest();
        test.TestCode = """
            using System.Runtime.CompilerServices;
            class Test
            {
                void A()
                {
                    {|TRX0134:B()|};
                }

                Sample B() => throw null;
            }

            interface ISample { }
            class Sample : ISample { }

            static class Extensions
            {
                public static TaskAwaiter GetAwaiter(this ISample value) => throw null;
            }
            """;

        return test.RunAsync();
    }

#if CSHARP14_OR_GREATER
    [Fact]
    public Task Report_GetAwaiterExtensionBlock()
    {
        var test = CreateTest();
        test.TestCode = """
            using System.Runtime.CompilerServices;
            class Test
            {
                void A()
                {
                    {|TRX0134:B()|};
                }

                Sample B() => throw null;
            }

            class Sample { }

            static class Extensions
            {
                extension(Sample value)
                {
                    public TaskAwaiter GetAwaiter() => throw null;
                }
            }
            """;

        return test.RunAsync();
    }
#endif

    [Fact]
    public Task NoReport_GetAwaiterExtensionMethod_OtherType()
    {
        var test = CreateTest();
        test.TestCode = """
            using System.Runtime.CompilerServices;
            class Test
            {
                void A()
                {
                    B();
                }

                Sample B() => throw null;
            }

            class Sample { }

            static class Extensions
            {
                public static TaskAwaiter GetAwaiter(this string value) => throw null;
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task NoReport_GetAwaiterExtensionMethod_NotInScope()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test
            {
                void A()
                {
                    B();
                }

                Sample B() => throw null;
            }

            class Sample { }

            namespace Other
            {
                static class Extensions
                {
                    public static System.Runtime.CompilerServices.TaskAwaiter GetAwaiter(this Sample value) => throw null;
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task Report_TypeParameterConstrainedToTask()
    {
        var test = CreateTest();
        test.TestCode = """
            using System.Threading.Tasks;
            class Test
            {
                void A<T>(System.Func<T> func) where T : Task
                {
                    {|TRX0134:func()|};
                }
            }
            """;

        return test.RunAsync();
    }
}
