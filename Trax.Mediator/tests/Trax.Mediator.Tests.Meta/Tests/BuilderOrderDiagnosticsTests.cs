using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Trax.Mediator.Extensions;

namespace Trax.Mediator.Tests.Meta.Tests;

/// <summary>
/// A builder call made in the wrong order fails to compile with an error that says which call
/// comes first, and the right order compiles clean. Each case compiles a small host in memory
/// against the Trax.Mediator this repo builds.
///
/// <para>Not ADR-enforcing: it pins the text of the compile errors that reference/builder-pattern documents for a wrong call order, a wording contract rather than a choice between designs.</para>
/// </summary>
[TestFixture]
public class BuilderOrderDiagnosticsTests
{
    private const string Prelude = """
        using System.Reflection;
        using Microsoft.Extensions.DependencyInjection;
        using Trax.Effect.Extensions;
        using Trax.Effect.StateMachine.Persistence;
        using Trax.Mediator.Extensions;

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
        yield return new TestCaseData(
            "trax.AddMediator(assembly)",
            "Call AddEffects(...) before AddMediator(...)."
        ).SetName("AddMediator(assemblies) before AddEffects");
        yield return new TestCaseData(
            "trax.AddMediator()",
            "Call AddEffects(...) before AddMediator(...)."
        ).SetName("AddMediator() before AddEffects");
        yield return new TestCaseData(
            "trax.AddMediator(assemblies: [assembly])",
            "Call AddEffects(...) before AddMediator(...)."
        ).SetName("AddMediator(assemblies: [...]) before AddEffects");
        yield return new TestCaseData(
            "trax.AddMediator(mediator => mediator.ScanAssemblies(assembly))",
            "Call AddEffects(...) before AddMediator(...)."
        ).SetName("AddMediator(configure) before AddEffects");
        yield return new TestCaseData(
            "trax.AddEffects().AddMediator(assembly).AddMediator(assembly)",
            "AddMediator(...) is already called. Call it once and configure everything in that call."
        ).SetName("AddMediator(assemblies) twice");
        yield return new TestCaseData(
            "trax.AddEffects().AddMediator(assembly).AddMediator(mediator => mediator)",
            "AddMediator(...) is already called. Call it once and configure everything in that call."
        ).SetName("AddMediator(configure) twice");
        yield return new TestCaseData(
            "trax.AddEffects().AddMediator(assembly).AddStateMachines(assembly)",
            "Call AddStateMachines(...) before AddMediator(...)."
        ).SetName("AddStateMachines(assemblies) after AddMediator");
        yield return new TestCaseData(
            "trax.AddEffects().AddMediator(assembly).AddStateMachines(options => { }, assembly)",
            "Call AddStateMachines(...) before AddMediator(...)."
        ).SetName("AddStateMachines(configure, assemblies) after AddMediator");
    }

    private static IEnumerable<TestCaseData> RightOrder()
    {
        yield return new TestCaseData("trax.AddEffects().AddMediator(assembly)").SetName(
            "AddEffects then AddMediator(assemblies)"
        );
        yield return new TestCaseData("trax.AddEffects().AddMediator()").SetName(
            "AddEffects then AddMediator()"
        );
        yield return new TestCaseData(
            "trax.AddEffects(effects => effects).AddMediator(assemblies: [assembly])"
        ).SetName("AddEffects(configure) then AddMediator(assemblies: [...])");
        yield return new TestCaseData(
            "trax.AddEffects().AddMediator(mediator => mediator.ScanAssemblies(assembly).SkipChainVerification())"
        ).SetName("AddEffects then AddMediator(configure)");
        yield return new TestCaseData(
            "trax.AddEffects().AddStateMachines(assembly).AddMediator(assembly)"
        ).SetName("AddStateMachines(assemblies) then AddMediator");
        yield return new TestCaseData(
            "trax.AddEffects().AddStateMachines(options => { }, assembly).AddMediator(assembly)"
        ).SetName("AddStateMachines(configure, assemblies) then AddMediator");
    }

    [TestCaseSource(nameof(WrongOrder))]
    public void WrongOrder_FailsWith_TheInstruction(string body, string instruction)
    {
        var errors = Compile(body);

        errors
            .Should()
            .ContainSingle(
                "a call made in the wrong order must fail with exactly one error, the one naming "
                    + "the fix, not CS1929 about builder state types. Errors:\n  "
                    + string.Join("\n  ", errors)
            )
            .Which.Should()
            .StartWith("CS0619: ")
            .And.EndWith($" is obsolete: '{instruction}'");
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
        typeof(BuilderOrderExtensions)
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
            .NotBeNull("every public member of BuilderOrderExtensions exists only to be refused");
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
            "BuilderOrderProbe",
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
