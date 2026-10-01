using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Trax.Scheduler.Extensions;

namespace Trax.Scheduler.Tests.Meta.Tests;

/// <summary>
/// <c>AddScheduler</c> called before <c>AddMediator</c> fails to compile with an error that says
/// which call comes first, and the right order compiles clean. Each case compiles a small host in
/// memory against the Trax.Scheduler this repo builds.
///
/// <para>Not ADR-enforcing: it pins the text of the compile errors that reference/builder-pattern documents for a wrong call order, a wording contract rather than a choice between designs.</para>
/// </summary>
[TestFixture]
public class SchedulerBuilderOrderDiagnosticsTests
{
    private const string Instruction = "Call AddMediator(...) before AddScheduler(...).";

    private const string Prelude = """
        using Microsoft.Extensions.DependencyInjection;
        using Trax.Effect.Extensions;
        using Trax.Mediator.Extensions;
        using Trax.Scheduler.Extensions;

        public static class Host
        {
            public static void Configure(IServiceCollection services)
            {
                var assembly = typeof(Host).Assembly;
                services.AddTrax(trax => { _ = BODY; });
            }
        }
        """;

    private static IEnumerable<TestCaseData> WrongOrder()
    {
        yield return new TestCaseData("trax.AddEffects().AddScheduler()").SetName(
            "AddScheduler() after AddEffects, before AddMediator"
        );
        yield return new TestCaseData(
            "trax.AddEffects().AddScheduler(scheduler => scheduler)"
        ).SetName("AddScheduler(configure) after AddEffects, before AddMediator");
        yield return new TestCaseData("trax.AddScheduler()").SetName(
            "AddScheduler() before AddEffects"
        );
        yield return new TestCaseData("trax.AddScheduler(scheduler => scheduler)").SetName(
            "AddScheduler(configure) before AddEffects"
        );
    }

    private static IEnumerable<TestCaseData> RightOrder()
    {
        yield return new TestCaseData(
            "trax.AddEffects().AddMediator(assembly).AddScheduler()"
        ).SetName("AddMediator then AddScheduler()");
        yield return new TestCaseData(
            "trax.AddEffects().AddMediator(assembly).AddScheduler(scheduler => scheduler)"
        ).SetName("AddMediator then AddScheduler(configure)");
    }

    [TestCaseSource(nameof(WrongOrder))]
    public void WrongOrder_FailsWith_TheInstruction(string body)
    {
        var errors = Compile(body);

        errors
            .Should()
            .ContainSingle(
                "AddScheduler before AddMediator must fail with exactly one error, the one naming "
                    + "the fix, not CS1929 about builder state types. Errors:\n  "
                    + string.Join("\n  ", errors)
            )
            .Which.Should()
            .StartWith("CS0619: ")
            .And.EndWith($" is obsolete: '{Instruction}'");
    }

    [TestCaseSource(nameof(RightOrder))]
    public void RightOrder_Compiles_WithoutDiagnostics(string body)
    {
        Compile(body)
            .Should()
            .BeEmpty(
                "the documented order must still bind to the real method with no error, warning "
                    + "or ambiguity introduced by the wrong-order overloads"
            );
    }

    private static IEnumerable<TestCaseData> WrongOrderOverloads() =>
        typeof(SchedulerBuilderOrderExtensions)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(method =>
                new TestCaseData(method).SetName(
                    $"{method.Name}({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))})"
                )
            );

    /// <summary>
    /// A call the compiler refuses can still be made through reflection or <c>dynamic</c>, which
    /// bind at run time and ignore <c>[Obsolete]</c>. Such a call gets the same instruction as the
    /// compile error, and every public member of the class is one of these refusals.
    /// </summary>
    [TestCaseSource(nameof(WrongOrderOverloads))]
    public void WrongOrderOverload_CalledAtRunTime_ThrowsTheSameInstruction(MethodInfo method)
    {
        var obsolete = method.GetCustomAttribute<ObsoleteAttribute>();

        obsolete
            .Should()
            .NotBeNull(
                "every public member of SchedulerBuilderOrderExtensions exists only to be refused"
            );
        obsolete!.IsError.Should().BeTrue();

        var call = () => method.Invoke(null, new object?[method.GetParameters().Length]);

        call.Should()
            .Throw<TargetInvocationException>()
            .WithInnerException<InvalidOperationException>()
            .Which.Message.Should()
            .Be(obsolete.Message, "the run-time refusal says the same thing as the compile error");
    }

    private static List<string> Compile(string body)
    {
        var tree = CSharpSyntaxTree.ParseText(
            Prelude.Replace("BODY", body, StringComparison.Ordinal),
            new CSharpParseOptions(LanguageVersion.Latest)
        );

        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));

        var compilation = CSharpCompilation.Create(
            "SchedulerBuilderOrderProbe",
            [tree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        return compilation
            .GetDiagnostics()
            .Where(d => d.Severity >= DiagnosticSeverity.Warning)
            .Select(d => $"{d.Id}: {d.GetMessage()}")
            .ToList();
    }
}
