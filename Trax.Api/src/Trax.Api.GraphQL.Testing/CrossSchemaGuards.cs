using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.EntityFrameworkCore;
using Trax.Api.GraphQL.DataLoaders.CrossSchema;
using Trax.Core.Testing;
using Trax.Core.Testing.Infrastructure;

namespace Trax.Api.GraphQL.Testing;

/// <summary>
/// Architecture-guard checkers for the cross-schema GraphQL pattern. <see cref="EdgeManifestIsValid"/>
/// reflects over a declared <see cref="CrossSchemaEdge"/> manifest; the source guards verify that edge
/// resolvers live in (and route through) the dedicated cross-schema project.
/// </summary>
public static class CrossSchemaGuards
{
    private static readonly Regex CamelCase = new("^[a-z][A-Za-z0-9]*$", RegexOptions.Compiled);

    /// <summary>
    /// Each edge in the manifest must reference a real integer foreign key on its source, a target
    /// owned (as a <c>DbSet</c>) by the declared target context, and a camelCase field name.
    /// </summary>
    public static GuardResult EdgeManifestIsValid(IReadOnlyList<CrossSchemaEdge> edges)
    {
        ArgumentNullException.ThrowIfNull(edges);
        var offenders = new List<string>();

        foreach (var edge in edges)
        {
            var fk = edge.Source.GetProperty(edge.Fk, BindingFlags.Public | BindingFlags.Instance);
            if (fk is null)
            {
                offenders.Add($"{edge.Source.Name}.{edge.Fk} does not exist");
            }
            else
            {
                var fkType = Nullable.GetUnderlyingType(fk.PropertyType) ?? fk.PropertyType;
                if (fkType != typeof(int))
                    offenders.Add($"{edge.Source.Name}.{edge.Fk} must be an int foreign key");
            }

            var ownsTarget = edge
                .TargetContext.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Any(p =>
                    p.PropertyType.IsGenericType
                    && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>)
                    && p.PropertyType.GetGenericArguments()[0] == edge.Target
                );
            if (!ownsTarget)
                offenders.Add(
                    $"{edge.TargetContext.Name} must expose DbSet<{edge.Target.Name}> (it is the declared owner)"
                );

            if (!CamelCase.IsMatch(edge.FieldName))
                offenders.Add($"edge field '{edge.FieldName}' must be camelCase");
        }

        var message =
            "Each cross-schema edge must map to a real int foreign key on its source, a DbSet-owned "
            + "target on the declared context, and a camelCase field. Offenders:\n  "
            + string.Join("\n  ", offenders);

        return new GuardResult(offenders, edges.Count, message);
    }

    /// <summary>
    /// Every resolver on a type extension in a cross-schema project must route through a
    /// <c>CrossSchemaLoader</c>, so a cross-schema field can never become a hidden N+1. Checked per
    /// resolver: a public method of an <c>[ExtendObjectType]</c> class (in any attribute list, with
    /// or without its namespace or type argument) or of a source-generated <c>[ObjectType&lt;T&gt;]</c>
    /// extension that is not <c>[GraphQLIgnore]</c>, and which names <c>CrossSchemaLoader&lt;,&gt;</c>
    /// in its parameters or body.
    /// </summary>
    public static GuardResult EdgeResolversUseLoader(
        ArchitectureGuardOptions options,
        string crossSchemaProjectSuffix = ".CrossSchema"
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        var root = options.RepoRootOverride ?? RepoRoot.Path;
        var offenders = new List<string>();
        var inspected = 0;

        foreach (var file in SourceFiles.CSharpUnder(root, [.. options.SourceScanRoots]))
        {
            var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (!rel.Contains($"{crossSchemaProjectSuffix}/", StringComparison.Ordinal))
                continue;

            foreach (var extension in TypeExtensions(file))
            foreach (var resolver in extension.Members.OfType<MethodDeclarationSyntax>())
            {
                if (!IsResolver(resolver))
                    continue;

                inspected++;
                if (!NamesLoader(resolver))
                    offenders.Add($"{rel}: {extension.Identifier.Text}.{resolver.Identifier.Text}");
            }
        }

        var message =
            $"Every resolver on a type extension in a {crossSchemaProjectSuffix} project must resolve "
            + "through a CrossSchemaLoader<>, never an ad-hoc DbContext query, so the field is batched. "
            + "Offenders:\n  "
            + string.Join("\n  ", offenders);

        return new GuardResult(offenders, inspected, message);
    }

    /// <summary>
    /// Every property a <c>[Parent]</c> resolver reads off its parent must reach it. Trax adds
    /// the entity key to the projection for hand-written resolvers automatically, so a resolver
    /// reading <c>Id</c> needs nothing; a resolver reading anything else — a cross-schema foreign
    /// key, a column it aggregates on — must declare it with <c>[Parent(requires: ...)]</c> or
    /// silently receive a default value.
    /// </summary>
    /// <remarks>
    /// Parses each file's syntax tree and checks each resolver on its own: the reads are the
    /// member accesses on the <c>[Parent]</c> parameter inside that method (<c>p.X</c>,
    /// <c>p?.X</c>, <c>p!.X</c>), method calls excluded, and the declared names come from that
    /// parameter's <c>requires</c> argument, a <c>nameof</c> or a string, compared ignoring case
    /// since a string names the GraphQL field. Source-scanning, so the key it recognises is the
    /// <c>Id</c> convention: an entity whose key is declared with <c>[Key]</c> under another name
    /// still needs an explicit <c>requires:</c> here.
    /// </remarks>
    public static GuardResult ExtensionResolversDeclareParentRequirements(
        ArchitectureGuardOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        var root = options.RepoRootOverride ?? RepoRoot.Path;
        var offenders = new List<string>();
        var inspected = 0;

        foreach (var file in SourceFiles.CSharpUnder(root, [.. options.SourceScanRoots]))
        {
            var rel = Path.GetRelativePath(root, file).Replace('\\', '/');

            foreach (var extension in TypeExtensions(file))
            foreach (var method in extension.Members.OfType<MethodDeclarationSyntax>())
            foreach (var parameter in method.ParameterList.Parameters)
            {
                var parent = Attributes(parameter.AttributeLists)
                    .FirstOrDefault(a => NameIs(a.Name, "Parent"));
                if (parent is null)
                    continue;

                inspected++;
                var name = parameter.Identifier.Text;
                var declared = RequiredNames(parent);

                foreach (var read in PropertyReads(method, name))
                {
                    if (read is "Id" || declared.Contains(read))
                        continue;

                    offenders.Add(
                        $"{rel}: {extension.Identifier.Text}.{method.Identifier.Text} reads "
                            + $"{name}.{read} without [Parent(requires: ...)]"
                    );
                }
            }
        }

        var message =
            "A resolver that reads a property of its [Parent] only receives it when projection "
            + "selected it. The entity key is added automatically; every other property must be "
            + "declared with [Parent(requires: nameof(Entity.Property))]. Offenders:\n  "
            + string.Join("\n  ", offenders);

        return new GuardResult(offenders, inspected, message);
    }

    /// <summary>
    /// The type extensions declared in <paramref name="file"/>: classes carrying
    /// <c>[ExtendObjectType]</c> in any form, or HotChocolate's source-generated
    /// <c>[ObjectType&lt;T&gt;]</c>. A quick text check skips files that cannot hold one before
    /// parsing.
    /// </summary>
    private static IEnumerable<ClassDeclarationSyntax> TypeExtensions(string file)
    {
        var text = File.ReadAllText(file);
        if (!text.Contains("ObjectType", StringComparison.Ordinal))
            return [];

        return CSharpSyntaxTree
            .ParseText(text)
            .GetRoot()
            .DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .Where(c => Attributes(c.AttributeLists).Any(IsTypeExtensionAttribute));
    }

    private static bool IsTypeExtensionAttribute(AttributeSyntax attribute) =>
        NameIs(attribute.Name, "ExtendObjectType")
        || (
            NameIs(attribute.Name, "ObjectType") && Unqualified(attribute.Name) is GenericNameSyntax
        );

    private static IEnumerable<AttributeSyntax> Attributes(SyntaxList<AttributeListSyntax> lists) =>
        lists.SelectMany(l => l.Attributes);

    /// <summary>
    /// Whether an attribute's name is <paramref name="name"/>, written with or without its
    /// namespace, its <c>Attribute</c> suffix or a type argument.
    /// </summary>
    private static bool NameIs(NameSyntax attributeName, string name)
    {
        var simple = Unqualified(attributeName).Identifier.Text;
        return simple == name || simple == name + "Attribute";
    }

    private static SimpleNameSyntax Unqualified(NameSyntax name) =>
        name switch
        {
            QualifiedNameSyntax qualified => qualified.Right,
            AliasQualifiedNameSyntax alias => alias.Name,
            _ => (SimpleNameSyntax)name,
        };

    /// <summary>
    /// A member HotChocolate exposes as a field: a public method that is not marked
    /// <c>[GraphQLIgnore]</c>.
    /// </summary>
    private static bool IsResolver(MethodDeclarationSyntax method) =>
        method.Modifiers.Any(SyntaxKind.PublicKeyword)
        && !Attributes(method.AttributeLists).Any(a => NameIs(a.Name, "GraphQLIgnore"));

    private static bool NamesLoader(MethodDeclarationSyntax method) =>
        method
            .DescendantNodes()
            .OfType<GenericNameSyntax>()
            .Any(g => g.Identifier.Text == "CrossSchemaLoader");

    /// <summary>
    /// The names a <c>[Parent]</c> attribute's <c>requires</c> argument declares (named or first
    /// positional): the last identifier of a <c>nameof</c>, or each identifier in a string.
    /// </summary>
    private static HashSet<string> RequiredNames(AttributeSyntax parent)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var arguments = parent.ArgumentList?.Arguments ?? default;
        var requires =
            arguments.FirstOrDefault(a => a.NameColon?.Name.Identifier.Text == "requires")
            ?? arguments.FirstOrDefault(a => a.NameColon is null && a.NameEquals is null);
        if (requires is null)
            return names;

        foreach (var node in requires.Expression.DescendantNodesAndSelf())
        {
            switch (node)
            {
                case InvocationExpressionSyntax
                {
                    Expression: IdentifierNameSyntax { Identifier.Text: "nameof" }
                } nameOf when nameOf.ArgumentList.Arguments.Count == 1:
                    names.Add(LastIdentifier(nameOf.ArgumentList.Arguments[0].Expression));
                    break;
                case LiteralExpressionSyntax literal
                    when literal.IsKind(SyntaxKind.StringLiteralExpression):
                    foreach (Match word in Identifier.Matches(literal.Token.ValueText))
                        names.Add(word.Value);
                    break;
            }
        }

        return names;
    }

    private static readonly Regex Identifier = new(@"[A-Za-z_]\w*", RegexOptions.Compiled);

    private static string LastIdentifier(ExpressionSyntax expression) =>
        (expression as MemberAccessExpressionSyntax)?.Name.Identifier.Text ?? expression.ToString();

    /// <summary>
    /// Distinct property names <paramref name="method"/> reads off its parameter
    /// <paramref name="parameterName"/>: <c>p.X</c>, <c>p?.X</c>, <c>p!.X</c> and
    /// <c>(p).X</c>. A method call (<c>p.Foo()</c>) is behaviour on the instance, not a projected
    /// column, and is not a read.
    /// </summary>
    private static IEnumerable<string> PropertyReads(
        MethodDeclarationSyntax method,
        string parameterName
    )
    {
        var reads = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in method.DescendantNodes())
        {
            switch (node)
            {
                case MemberAccessExpressionSyntax member
                    when IsParameter(member.Expression, parameterName) && !IsCalled(member):
                    reads.Add(member.Name.Identifier.Text);
                    break;
                case ConditionalAccessExpressionSyntax conditional
                    when IsParameter(conditional.Expression, parameterName)
                        && FirstBinding(conditional.WhenNotNull) is { } nested
                        && nested.Parent is not InvocationExpressionSyntax:
                    reads.Add(nested.Name.Identifier.Text);
                    break;
            }
        }
        return reads;
    }

    /// <summary>The leftmost member binding of a chain such as <c>?.A.B</c> or <c>?.A()</c>.</summary>
    private static MemberBindingExpressionSyntax? FirstBinding(ExpressionSyntax expression) =>
        expression
            .DescendantNodesAndSelf()
            .OfType<MemberBindingExpressionSyntax>()
            .FirstOrDefault();

    private static bool IsCalled(MemberAccessExpressionSyntax member) =>
        member.Parent is InvocationExpressionSyntax invocation && invocation.Expression == member;

    private static bool IsParameter(ExpressionSyntax expression, string parameterName) =>
        expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.Text == parameterName,
            PostfixUnaryExpressionSyntax
            {
                RawKind: (int)SyntaxKind.SuppressNullableWarningExpression
            } suppressed => IsParameter(suppressed.Operand, parameterName),
            ParenthesizedExpressionSyntax parenthesized => IsParameter(
                parenthesized.Expression,
                parameterName
            ),
            _ => false,
        };
}
