using System.Reflection;
using HotChocolate.Authorization;
using HotChocolate.Configuration;
using HotChocolate.Internal;
using HotChocolate.Types.Descriptors.Configurations;
using Trax.Api.GraphQL.Mutations;
using Trax.Api.GraphQL.Queries;
using Trax.Api.GraphQL.Subscriptions;
using Trax.Effect.Attributes;

namespace Trax.Api.GraphQL.Configuration;

/// <summary>
/// Makes <c>[TraxAuthorize]</c> and <c>[TraxAllowAnonymous]</c> work on a resolver, and censuses
/// the fields a type extension adds to a type Trax owns.
/// </summary>
/// <remarks>
/// <para>
/// Two phases, at two different points in HotChocolate's pipeline because they need different
/// things. Emission runs at <c>OnBeforeRegisterDependencies</c>, on the extension's own
/// configuration, which is early enough that the <c>@authorize</c> directive is registered as a
/// dependency and turned into resolver middleware. The census runs at
/// <c>OnBeforeCompleteType</c>, on the merged type, which is the only point where a field's
/// parent, and therefore what it inherits, is known.
/// </para>
/// <para>
/// Reading the merged type rather than the registered <c>[ExtendObjectType]</c> classes is what
/// makes the census complete. <c>ConfigureSchema</c> hands the consumer the whole
/// <see cref="HotChocolate.Execution.Configuration.IRequestExecutorBuilder"/>, so a type
/// extension can reach the schema without passing through
/// <c>GraphQLConfiguration.AdditionalTypeExtensions</c>, and a census built from that list would
/// not know it exists.
/// </para>
/// </remarks>
internal sealed class TypeExtensionExposureInterceptor : TypeInterceptor
{
    private readonly GraphQLConfiguration _configuration;
    private readonly TypeExtensionExposureReport _report;
    private readonly Dictionary<Type, TypeExtensionParentPosture> _postureByEntityType;

    /// <summary>
    /// The schema root types Trax registers. A field grafted onto one of these has no parent to
    /// inherit from, which is why a subscription added by
    /// <c>[ExtendObjectType("LifecycleSubscriptions")]</c> is covered without a special case.
    /// </summary>
    private static readonly HashSet<Type> RootTypes =
    [
        typeof(RootQuery),
        typeof(RootMutation),
        typeof(LifecycleSubscriptions),
    ];

    public TypeExtensionExposureInterceptor(
        GraphQLConfiguration configuration,
        TypeExtensionExposureReport report
    )
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(report);

        _configuration = configuration;
        _report = report;

        // Last registration wins, matching how the rest of the pipeline resolves a duplicate
        // entity registration, rather than throwing here for a conflict the census did not cause.
        _postureByEntityType = new Dictionary<Type, TypeExtensionParentPosture>();
        foreach (var reg in configuration.ModelRegistrations)
        {
            _postureByEntityType[reg.EntityType] =
                reg.AuthorizeAttributes.Count > 0 ? TypeExtensionParentPosture.Gated
                : reg.AllowAnonymous ? TypeExtensionParentPosture.Anonymous
                : TypeExtensionParentPosture.NotExposed;
        }
    }

    // ── Phase 1: turn [TraxAuthorize] on a resolver into @authorize ──────

    /// <summary>
    /// Runs before type extensions are merged, so the fields seen here are the ones the extension
    /// itself declares and the directive travels with them into the merged type.
    /// </summary>
    public override void OnBeforeRegisterDependencies(
        ITypeDiscoveryContext discoveryContext,
        TypeSystemConfiguration configuration
    )
    {
        if (configuration is not ObjectTypeConfiguration objectType)
            return;

        foreach (var field in objectType.Fields)
        {
            if (Resolver(field) is not { } resolver)
                continue;

            var declaration = Declaration(objectType.RuntimeType, resolver);
            if (!declaration.HasAuthorize)
                continue;

            Emit(discoveryContext, field, declaration.Authorize);
        }
    }

    /// <summary>
    /// Emits one <c>@authorize</c> per policy plus a single unioned roles directive, matching
    /// <see cref="AuthorizeDirectives"/> exactly so a train, an entity and a resolver carrying the
    /// same attribute get the same rules.
    /// </summary>
    private static void Emit(
        ITypeDiscoveryContext context,
        ObjectFieldConfiguration field,
        IReadOnlyList<TraxAuthorizeAttribute> attributes
    )
    {
        AuthorizeDirectives.ExtractRules(attributes, out var policies, out var roles);

        // ConfigurationHelper is how HotChocolate itself turns a directive instance into a
        // configuration: it builds the type reference from the inspector, which is not something
        // a caller can construct.
        var inspector = context.TypeInspector;

        foreach (var policy in policies)
            field.AddDirective(
                new AuthorizeDirective(policy, apply: ApplyPolicy.BeforeResolver),
                inspector
            );

        if (roles.Length > 0)
            field.AddDirective(
                new AuthorizeDirective(roles, apply: ApplyPolicy.BeforeResolver),
                inspector
            );
        else if (policies.Length == 0)
            field.AddDirective(new AuthorizeDirective(ApplyPolicy.BeforeResolver), inspector);
    }

    // ── Phase 2: the census, on the merged type ─────────────────────────

    /// <summary>
    /// Runs after type extensions are merged, so a consumer-supplied <c>[ExtendObjectType]</c>
    /// field is present on the object type by this point.
    /// </summary>
    public override void OnBeforeCompleteType(
        ITypeCompletionContext completionContext,
        TypeSystemConfiguration configuration
    )
    {
        if (configuration is not ObjectTypeConfiguration objectType)
            return;

        var parent = ResolveParentPosture(objectType.RuntimeType);

        foreach (var field in objectType.Fields)
        {
            // Introspection fields resolve off the type system, not off a parent instance.
            if (field.Name.StartsWith("__", StringComparison.Ordinal))
                continue;

            if (ExtensionMember(objectType.RuntimeType, field) is not { } member)
                continue;

            var fieldPath = $"{objectType.Name}.{field.Name}";
            var resolver = $"{member.DeclaringType?.FullName}.{member.Name}";
            var declaration = Declaration(objectType.RuntimeType, member);

            // Another framework's attribute is refused wherever it appears, gated parent or not:
            // it is a posture Trax cannot enforce, standing where a Trax one belongs.
            if (declaration.HasForeign)
            {
                _report.Add(
                    new TypeExtensionExposureViolation(
                        fieldPath,
                        TraxAuthorization.ForeignAttributeMessage(
                            $"GraphQL field '{fieldPath}' ({resolver})",
                            declaration.ForeignAttributes
                        )
                    )
                );
                continue;
            }

            var violation = TypeExtensionExposureRule.Evaluate(
                parent,
                declaration.HasAuthorize,
                declaration.AllowAnonymous,
                _configuration.AuthorizationRequired
            );

            if (violation is ExposureViolation.None)
                continue;

            _report.Add(
                new TypeExtensionExposureViolation(
                    fieldPath,
                    TypeExtensionExposureRule.BuildMessage(
                        fieldPath,
                        resolver,
                        DescribeParent(objectType.RuntimeType, parent),
                        violation
                    )
                )
            );
        }
    }

    // ── Reading a declaration ───────────────────────────────────────────

    /// <summary>
    /// The posture a resolver declares: its own attributes, plus the ones on its
    /// <c>[ExtendObjectType]</c> class.
    /// </summary>
    /// <remarks>
    /// The class-level half is the reason Trax owns this attribute rather than deferring to
    /// HotChocolate's. HotChocolate applies a class-level attribute to the type being extended, so
    /// on a <c>[TraxAllowAnonymous]</c> entity it re-locks the whole entity and on a root type it
    /// sets the posture of every operation in the schema. <c>[TraxAuthorize]</c> on an extension
    /// class applies to the fields that extension contributes, which is what someone writing it
    /// there means. The declaring type is only consulted when it is not the type being extended,
    /// so an entity's own class attribute is left to the type-level directive that already
    /// carries it.
    /// </remarks>
    private static TraxAuthorizationDeclaration Declaration(Type runtimeType, MemberInfo member)
    {
        var own = member is MethodInfo method
            ? TraxAuthorization.Read(method)
            : new TraxAuthorizationDeclaration([], false, []);

        if (member.DeclaringType is not { } declaring || declaring == runtimeType)
            return own;

        var fromClass = TraxAuthorization.Read(declaring);

        return new TraxAuthorizationDeclaration(
            [.. own.Authorize, .. fromClass.Authorize],
            own.AllowAnonymous || fromClass.AllowAnonymous,
            [
                .. own
                    .ForeignAttributes.Concat(fromClass.ForeignAttributes)
                    .Distinct(StringComparer.Ordinal),
            ]
        );
    }

    /// <summary>
    /// The resolver behind a field, when it is a method Trax can read attributes off. A field with
    /// no member is a resolver built inline (Trax's own <c>discover</c> and <c>operations</c>
    /// entry fields are built this way), which has no declaration site for an attribute.
    /// </summary>
    private static MethodInfo? Resolver(ObjectFieldConfiguration field) =>
        (field.ResolverMember ?? field.Member) as MethodInfo;

    /// <summary>
    /// The CLR member behind a field that a type extension contributed, or <c>null</c> when the
    /// field is not one.
    /// </summary>
    /// <remarks>
    /// A member declared by the object's own runtime type, or by a base or interface of it, is a
    /// natural member of the type and carries the type's own posture. A member declared anywhere
    /// else was bolted on.
    /// </remarks>
    private static MemberInfo? ExtensionMember(Type runtimeType, ObjectFieldConfiguration field)
    {
        var member = field.ResolverMember ?? field.Member;

        return member?.DeclaringType is { } declaring && !declaring.IsAssignableFrom(runtimeType)
            ? member
            : null;
    }

    private TypeExtensionParentPosture ResolveParentPosture(Type runtimeType)
    {
        // A root type has nothing above it, so a field on one inherits nothing.
        if (RootTypes.Contains(runtimeType))
            return TypeExtensionParentPosture.Anonymous;

        return _postureByEntityType.TryGetValue(runtimeType, out var posture)
            ? posture
            : TypeExtensionParentPosture.NotExposed;
    }

    private static string DescribeParent(Type runtimeType, TypeExtensionParentPosture parent) =>
        parent is TypeExtensionParentPosture.Anonymous && RootTypes.Contains(runtimeType)
            ? $"the schema root type '{runtimeType.Name}'"
            : $"'{runtimeType.Name}', which is [TraxAllowAnonymous]";
}
