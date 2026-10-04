using System.Reflection;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Trax.Effect.Data.Extensions;

namespace Trax.Effect.Tests.Meta.Tests;

/// <summary>
/// A builder call made in the wrong order fails to compile with an error that says which call
/// comes first, and the right order compiles clean. Each case compiles a small host in memory
/// against the Trax.Effect this repo builds.
///
/// <para>Not ADR-enforcing: it pins the text of the compile errors that reference/builder-pattern documents for a wrong call order, a wording contract rather than a choice between designs.</para>
/// </summary>
[TestFixture]
public class BuilderOrderDiagnosticsTests
{
    private const string Prelude = """
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Extensions.Logging;
        using Trax.Effect.Data.Extensions;
        using Trax.Effect.Data.InMemory.Extensions;
        using Trax.Effect.Data.Postgres.Extensions;
        using Trax.Effect.Extensions;
        using Trax.Effect.Provider.Parameter.Extensions;

        public static class Host
        {
            public static void Configure(IServiceCollection services)
            {
                services.AddTrax(trax => trax.AddEffects(effects => BODY));
            }
        }
        """;

    private const string DataProviderFirst =
        "Call UsePostgres(...), UseSqlite(...) or UseInMemory(...) before AddDataContextLogging(...).";

    private static IEnumerable<TestCaseData> WrongOrder()
    {
        yield return new TestCaseData("effects.AddDataContextLogging()", DataProviderFirst).SetName(
            "AddDataContextLogging() with no data provider"
        );
        yield return new TestCaseData(
            "effects.AddDataContextLogging(LogLevel.Warning).UseInMemory()",
            DataProviderFirst
        ).SetName("AddDataContextLogging(level) before UseInMemory");
        yield return new TestCaseData(
            "effects.AddDataContextLogging(blacklist: [\"Noisy.*\"]).UsePostgres(\"Host=x\")",
            DataProviderFirst
        ).SetName("AddDataContextLogging(blacklist) before UsePostgres");
        yield return new TestCaseData(
            "effects.SaveTrainParameters().AddDataContextLogging()",
            DataProviderFirst
        ).SetName("AddDataContextLogging after a general effect and no data provider");
    }

    private static IEnumerable<TestCaseData> RightOrder()
    {
        yield return new TestCaseData("effects.UseInMemory().AddDataContextLogging()").SetName(
            "UseInMemory then AddDataContextLogging()"
        );
        yield return new TestCaseData(
            "effects.UsePostgres(\"Host=x\").AddDataContextLogging(minimumLogLevel: LogLevel.Warning, blacklist: [\"Noisy.*\"])"
        ).SetName("UsePostgres then AddDataContextLogging(level, blacklist)");
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
