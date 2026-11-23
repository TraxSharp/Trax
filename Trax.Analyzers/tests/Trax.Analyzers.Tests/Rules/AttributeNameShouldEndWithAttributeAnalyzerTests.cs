using CodeFixTest = Trax.Analyzers.Tests.Harness.CSharpCodeFixTest<
    Trax.Analyzers.Rules.AttributeNameShouldEndWithAttributeAnalyzer,
    Trax.Analyzers.Rules.TypeNameShouldEndWithSuffixFixer>;

namespace Trax.Analyzers.Tests.Rules;

public sealed class AttributeNameShouldEndWithAttributeAnalyzerTests
{
    private static CodeFixTest CreateTest() => new();

    [Fact]
    public Task NameEndsWithAttribute()
    {
        var test = CreateTest();
        test.TestCode = """
            class CustomAttribute : System.Attribute
            {
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task NameDoesNotEndWithAttribute()
    {
        var test = CreateTest();
        test.TestCode = """
            class {|TRX0057:CustomAttr|} : System.Attribute
            {
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task NameDoesNotEndWithAttribute_CodeFix()
    {
        var test = CreateTest();
        test.TestCode = """
            class {|TRX0057:CustomAttr|} : System.Attribute
            {
            }
            """;
        test.FixedCode = """
            class CustomAttrAttribute : System.Attribute
            {
            }
            """;

        return test.RunAsync();
    }
}
