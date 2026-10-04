using HotChocolate.Language;
using HotChocolate.Language.Visitors;

namespace Trax.Api.GraphQL.Audit;

/// <summary>
/// Rewrites a GraphQL document so that every string, integer and float literal is replaced by a
/// placeholder: <c>""</c> for a string, <c>0</c> for a number. Field names, aliases, arguments,
/// input-object field names, variable references, booleans, enum values and <c>null</c> are kept,
/// so the record still shows what was called and which inputs were set, but not their values.
/// </summary>
/// <remarks>
/// This is Apollo's <c>stripSensitiveLiterals</c>, the transform behind its operation signatures,
/// with list and object literals kept rather than emptied: an audit record should show which
/// input fields a call set. It reaches every value in the document, including inline arguments,
/// nested input objects, list items, directive arguments and variable default values, through
/// HotChocolate's own <see cref="SyntaxRewriter{TContext}"/>. That rewriter returns a directive
/// unchanged rather than descending into its arguments, so <see cref="RewriteDirective"/> does
/// it here, for a directive at every location: operation, variable definition, field, fragment
/// spread, inline fragment and fragment definition.
/// See <c>docs/adr/0027-an-audit-entry-records-no-value-the-caller-sent-unless-the-host-opts-in.md</c>.
/// </remarks>
internal sealed class AuditLiteralStripper : SyntaxRewriter<object?>
{
    public static readonly AuditLiteralStripper Instance = new();

    private static readonly StringValueNode StringPlaceholder = new(string.Empty);
    private static readonly IntValueNode IntPlaceholder = new(0);
    private static readonly FloatValueNode FloatPlaceholder = new(0d);

    /// <summary>
    /// Returns <paramref name="document"/> with every string and numeric literal replaced. Never
    /// falls back to the original text: if the rewriter produced no document it throws, and the
    /// listener counts the entry as dropped.
    /// </summary>
    public static DocumentNode Strip(DocumentNode document) =>
        Instance.RewriteDocument(document, null)
        ?? throw new InvalidOperationException("The audit literal stripper produced no document.");

    /// <summary>
    /// Rewrites each argument of the directive like an argument of a field, so its literals are
    /// replaced. The base rewriter returns a directive as it is.
    /// </summary>
    protected override DirectiveNode? RewriteDirective(DirectiveNode node, object? context) =>
        node.WithArguments([
            .. node.Arguments.Select(a => RewriteArgument(a, context)).OfType<ArgumentNode>(),
        ]);

    protected override StringValueNode RewriteStringValue(StringValueNode node, object? context) =>
        StringPlaceholder;

    protected override IntValueNode RewriteIntValue(IntValueNode node, object? context) =>
        IntPlaceholder;

    protected override FloatValueNode RewriteFloatValue(FloatValueNode node, object? context) =>
        FloatPlaceholder;
}
