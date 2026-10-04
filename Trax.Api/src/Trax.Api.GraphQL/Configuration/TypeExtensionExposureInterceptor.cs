using System.Reflection;
using HotChocolate.Authorization;
using HotChocolate.Configuration;
using HotChocolate.Internal;
using HotChocolate.Types.Descriptors.Configurations;
using Trax.Api.GraphQL.Mutations;
using Trax.Api.GraphQL.Queries;
using Trax.Api.GraphQL.Subscriptions;
using Trax.Api.GraphQL.TypeModules;
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

    /// <summary>
    /// The namespaces Trax hangs off a root type through an ungated field (<c>discover</c>,
    /// <c>dispatch</c>). A field grafted onto one is as reachable as a field on the root itself.
    /// The per-namespace types Trax builds under them have runtime type <c>object</c>, so those
    /// are recognised by the marker <see cref="NamespaceTypes.Base"/> sets on each one.
    /// </summary>
    private static readonly HashSet<Type> RootNamespaceTypes =
    [
        typeof(DiscoverQueries),
        typeof(DispatchMutations),
    ];

    /// <summary>
    /// The types under the <c>operations</c> field, whose posture is the one the host declared
    /// for the whole namespace (api/0004).
    /// </summary>
    private static readonly HashSet<Type> OperationsTypes =
    [
        typeof(OperationsQueries),
        typeof(DeadLetterQueries),
        typeof(WorkQueueQueries),
        typeof(ManifestGroupQueries),
        typeof(LogQueries),
        typeof(MetricsQueries),
        typeof(ConfigQueries),
        typeof(OperationsMutations),
        typeof(DeadLetterMutations),
        typeof(WorkQueueMutations),
        typeof(ManifestGroupMutations),
        typeof(ConfigMutations),
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

        // A query model's navigation target declares its own posture (api/0025), so an extension
        // of it inherits that posture exactly as an extension of a model does. Models are entered
        // after, so a type that is both takes the model's posture.
        _postureByEntityType = new Dictionary<Type, TypeExtensionParentPosture>();
        foreach (var target in configuration.NavigationTargets)
        {
            _postureByEntityType[target.EntityType] =
                target.IsGated ? TypeExtensionParentPosture.Gated
                : target.AllowAnonymous ? TypeExtensionParentPosture.Anonymous
                : TypeExtensionParentPosture.NotExposed;
        }

        // Last registration wins, matching how the rest of the pipeline resolves a duplicate
        // entity registration, rather than throwing here for a conflict the census did not cause.
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

            if (MalformedAttribute(declaration.Authorize) is { } malformed)
            {
                _report.Add(
                    MalformedViolation(
                        $"{objectType.Name}.{field.Name}",
                        $"{resolver.DeclaringType?.FullName}.{resolver.Name}",
                        malformed
                    )
                );
                continue;
            }

            AuthorizeDirectives.Emit(field, declaration.Authorize, discoveryContext.TypeInspector);
        }
    }

    /// <summary>
    /// Describes the first attribute whose shape would build a different gate from the one it
    /// reads as: an empty policy name, or a role list that names no role. Either one dropped would
    /// leave a gate that asks for less, so the field is refused instead, as the same attribute on
    /// a query model is.
    /// </summary>
    private static string? MalformedAttribute(IReadOnlyList<TraxAuthorizeAttribute> attributes)
    {
        foreach (var attribute in attributes)
        {
            if (attribute.Policy is not null && string.IsNullOrWhiteSpace(attribute.Policy))
                return "[TraxAuthorize] with an empty Policy";

            if (
                attribute.Roles is not null
                && AuthorizeDirectives.ParseRoles(attribute.Roles).Length == 0
            )
                return $"[TraxAuthorize(Roles = \"{attribute.Roles}\")], which names no role";
        }

        return null;
    }

    private static TypeExtensionExposureViolation MalformedViolation(
        string fieldPath,
        string resolver,
        string malformed
    ) =>
        new(
            fieldPath,
            $"GraphQL field '{fieldPath}' ({resolver}) declares {malformed}, so the gate it "
                + "means cannot be built. Remove the argument or name a real value."
        );

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

        var parent = ResolveParentPosture(objectType);

        foreach (var field in objectType.Fields)
        {
            // Introspection fields resolve off the type system, not off a parent instance.
            if (field.Name.StartsWith("__", StringComparison.Ordinal))
                continue;

            if (ExtensionMember(objectType.RuntimeType, field) is not { } member)
            {
                CensusOwnSubscriptionField(objectType, field);
                continue;
            }

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

            // Phase 1 refused to build this field's gate; say why rather than that it is missing.
            if (MalformedAttribute(declaration.Authorize) is { } malformed)
            {
                _report.Add(MalformedViolation(fieldPath, resolver, malformed));
                continue;
            }

            var violation = TypeExtensionExposureRule.Evaluate(
                parent,
                declaration.HasAuthorize,
                declaration.AllowAnonymous,
                _configuration.AuthorizationRequired
            );

            if (violation is ExposureViolation.None)
            {
                // The census accepted a [TraxAuthorize] field on the strength of the gate Phase 1
                // emits, so the gate has to be there. Asserting it keeps the two phases from
                // reading different members again.
                if (
                    declaration.HasAuthorize
                    && !declaration.AllowAnonymous
                    && !field.Directives.Any(d => d.Value is AuthorizeDirective)
                )
                    _report.Add(
                        new TypeExtensionExposureViolation(
                            fieldPath,
                            $"GraphQL field '{fieldPath}' ({resolver}) declares [TraxAuthorize], "
                                + "but no @authorize directive was emitted for it, so the field "
                                + "would be served ungated. This is a defect in Trax, not in the "
                                + "host: report it."
                        )
                    );
                continue;
            }

            _report.Add(
                new TypeExtensionExposureViolation(
                    fieldPath,
                    TypeExtensionExposureRule.BuildMessage(
                        fieldPath,
                        resolver,
                        DescribeParent(objectType, parent),
                        violation
                    )
                )
            );
        }
    }

    /// <summary>
    /// Trax's own subscription fields stream data the schema's other gates protect, so each must
    /// declare how it is authorized: per subscriber, or with a Trax posture attribute. Checking the
    /// type Trax itself registers means a field added to it later cannot ship ungated.
    /// </summary>
    private void CensusOwnSubscriptionField(
        ObjectTypeConfiguration objectType,
        ObjectFieldConfiguration field
    )
    {
        if (objectType.RuntimeType != typeof(LifecycleSubscriptions))
            return;

        if ((field.ResolverMember ?? field.Member) is not MethodInfo method)
            return;

        if (method.IsDefined(typeof(AuthorizedPerSubscriberAttribute), inherit: false))
            return;

        var declaration = TraxAuthorization.Read(method);
        if (declaration.HasAuthorize || declaration.AllowAnonymous)
            return;

        var fieldPath = $"{objectType.Name}.{field.Name}";
        _report.Add(
            new TypeExtensionExposureViolation(
                fieldPath,
                $"GraphQL subscription field '{fieldPath}' ({method.DeclaringType?.FullName}."
                    + $"{method.Name}) declares no authorization. A field on Trax's subscription "
                    + "root must be [AuthorizedPerSubscriber], [TraxAuthorize] or "
                    + "[TraxAllowAnonymous]. See "
                    + "docs/adr/0011-subscriptions-carry-the-authorization-of-the-data-they-stream.md."
            )
        );
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
    /// The member behind a field, method or property, that Trax reads a posture off. A property
    /// carries no attribute of its own (<c>[TraxAuthorize]</c> does not apply to one), but a field
    /// HotChocolate builds from an extension class's property takes the class-level posture
    /// exactly as a method does, so emission and the census read the same members. A field with
    /// no member is a resolver built inline (Trax's own <c>discover</c> and <c>operations</c>
    /// entry fields are built this way), which has no declaration site for an attribute.
    /// </summary>
    private static MemberInfo? Resolver(ObjectFieldConfiguration field) =>
        field.ResolverMember ?? field.Member;

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

    private TypeExtensionParentPosture ResolveParentPosture(ObjectTypeConfiguration objectType)
    {
        var runtimeType = objectType.RuntimeType;

        // A root type has nothing above it, so a field on one inherits nothing; nor does a field
        // on a namespace Trax reaches from the root through an ungated field.
        if (IsRootLike(objectType))
            return TypeExtensionParentPosture.Anonymous;

        // The operations namespace always carries a declared posture before the schema exists:
        // the builder refuses to expose it without GateOperations(), RequireAuthorization() or
        // AllowAnonymousOperations() (api/0004). A field grafted onto it inherits that decision.
        // Without a gate of its own the namespace is as reachable as the root it hangs off: under
        // RequireAuthorization() the endpoint gate covers the field, and under
        // AllowAnonymousOperations() nothing does, so the field declares, as on a root type.
        if (OperationsTypes.Contains(runtimeType))
            return _configuration.OperationsAuthorizeAttributes.Count > 0
                ? TypeExtensionParentPosture.Gated
                : TypeExtensionParentPosture.Anonymous;

        return _postureByEntityType.TryGetValue(runtimeType, out var posture)
            ? posture
            : TypeExtensionParentPosture.NotExposed;
    }

    private bool IsRootLike(ObjectTypeConfiguration objectType) =>
        RootTypes.Contains(objectType.RuntimeType)
        || RootNamespaceTypes.Contains(objectType.RuntimeType)
        || NamespaceTypes.IsDeclaredNamespace(objectType);

    private string DescribeParent(
        ObjectTypeConfiguration objectType,
        TypeExtensionParentPosture parent
    ) =>
        parent is not TypeExtensionParentPosture.Anonymous ? $"'{objectType.Name}'"
        : RootTypes.Contains(objectType.RuntimeType) ? $"the schema root type '{objectType.Name}'"
        : IsRootLike(objectType)
            ? $"'{objectType.Name}', a namespace reached from the schema root through an ungated field"
        : OperationsTypes.Contains(objectType.RuntimeType)
            ? $"'{objectType.Name}', under the operations namespace, which "
                + "AllowAnonymousOperations() leaves without a gate of its own"
        : $"'{objectType.Name}', which is [TraxAllowAnonymous]";
}
