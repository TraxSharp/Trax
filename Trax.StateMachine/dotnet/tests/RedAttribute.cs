// Linked into every test project of this folder by tests/Directory.Build.props. Each folder keeps its own
// identical copy, because a folder never references another folder's tests.
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;
using NUnit.Framework.Internal.Commands;

namespace Trax.Testing.Red;

/// <summary>
/// Marks a test written before the code it tests: it runs, and its verdict is inverted. A red test that fails or
/// errors is reported as passed, with the original failure's first line in the message; a red test that passes is
/// reported as failed, because the code has arrived and the attribute must come off. A skipped, ignored or
/// inconclusive result is left as it is. The test also carries the category <c>Red</c> and the property
/// <c>red</c> holding <paramref name="because"/>.
/// </summary>
/// <remarks>
/// NUnit reads a method wrapper from the test method only, so the attribute goes on each test method; it cannot
/// be put on a fixture. It applies to every case of a <c>[TestCase]</c> or <c>[TestCaseSource]</c> method, and
/// to an async test once its task has completed.
/// </remarks>
/// <param name="because">What the test is waiting for, shown in its result message.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
internal sealed class RedAttribute(string because) : Attribute, IWrapTestMethod, IApplyToTest
{
    /// <summary>The category every red test carries.</summary>
    public const string Category = "Red";

    /// <summary>The name of the property holding why the test is red.</summary>
    public const string PropertyName = "red";

    /// <summary>What the test is waiting for.</summary>
    public string Because { get; } = because;

    /// <inheritdoc/>
    public void ApplyToTest(Test test)
    {
        test.Properties.Add(PropertyNames.Category, Category);
        test.Properties.Add(PropertyName, Because);
    }

    /// <inheritdoc/>
    public TestCommand Wrap(TestCommand command) => new InvertedVerdictCommand(command, Because);

    private sealed class InvertedVerdictCommand(TestCommand inner, string because)
        : DelegatingTestCommand(inner)
    {
        public override TestResult Execute(TestExecutionContext context)
        {
            try
            {
                context.CurrentResult = innerCommand.Execute(context);
            }
            catch (Exception ex)
            {
                // The test method's own exception (an assertion included) reaches here: nothing inside the
                // wrapper records it.
                context.CurrentResult.RecordException(ex);
            }

            var result = context.CurrentResult;
            var state = result.ResultState;

            if (state.Status == TestStatus.Failed && state.Label != ResultState.Cancelled.Label)
                result.SetResult(
                    ResultState.Success,
                    $"Red as expected ({because}): {FirstLine(result.Message)}"
                );
            else if (state.Status == TestStatus.Passed)
                result.SetResult(
                    ResultState.Failure,
                    $"Passed while marked [Red] ({because}): remove the attribute"
                );

            return result;
        }

        private static string FirstLine(string? message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return "(no message)";
            var trimmed = message.TrimStart();
            var end = trimmed.IndexOfAny(['\r', '\n']);
            return end < 0 ? trimmed : trimmed[..end];
        }
    }
}
