using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Trax.Core.Testing;
using Trax.Core.Testing.Infrastructure;
using Trax.Effect.Attributes;

namespace Trax.Effect.Data.Testing;

/// <summary>
/// Architecture-guard checkers for the data layer. Source-scanning guards verify the shape of domain
/// contexts on disk; the reflection guard verifies the EF model. Each returns a
/// <see cref="GuardResult"/> the consumer asserts on with its own test framework.
/// </summary>
public static class DataLayerGuards
{
    private const string DefaultBaseTypeName = "DomainDataContext";

    private static readonly Regex ContextClass = new(
        @"\bclass\s+(\w+DbContext)\b",
        RegexOptions.Compiled
    );

    /// <summary>
    /// Every domain <c>*DbContext</c> under the source scan roots must derive the shared base
    /// (<c>DomainDataContext&lt;TSelf&gt;</c>), which enforces one-project-one-schema-one-context.
    /// </summary>
    public static GuardResult DomainContextsDeriveBase(
        ArchitectureGuardOptions options,
        string baseTypeName = DefaultBaseTypeName,
        IReadOnlySet<string>? knownExceptions = null
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        knownExceptions ??= new HashSet<string>(StringComparer.Ordinal);

        var root = options.RepoRootOverride ?? RepoRoot.Path;
        var inheritsBase = new Regex($@":\s*{Regex.Escape(baseTypeName)}<", RegexOptions.Compiled);
        var offenders = new List<string>();
        var inspected = 0;

        foreach (var file in SourceFiles.CSharpUnder(root, [.. options.SourceScanRoots]))
        {
            var stripped = SourceText.StripCommentsAndStrings(File.ReadAllText(file));
            if (!ContextClass.IsMatch(stripped))
                continue;

            inspected++;
            var rel = Rel(root, file);
            if (knownExceptions.Contains(rel))
                continue;

            if (!inheritsBase.IsMatch(stripped))
                offenders.Add(rel);
        }

        var message =
            $"Every domain *DbContext must derive {baseTypeName}<TSelf> (one project : one schema : "
            + "one context). Add the base, implement Schema and ConfigureModel(...). If a context "
            + "legitimately cannot, pass it in knownExceptions with a justification. Offenders:\n  "
            + string.Join("\n  ", offenders);

        return new GuardResult(offenders, inspected, message);
    }

    /// <summary>
    /// Every context deriving the shared base must ship a companion <c>I{Name}</c> interface in the
    /// same directory (application code depends on the interface, not the concrete context).
    /// </summary>
    public static GuardResult CompanionInterfaces(
        ArchitectureGuardOptions options,
        string baseTypeName = DefaultBaseTypeName
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        var root = options.RepoRootOverride ?? RepoRoot.Path;
        var baseContextClass = new Regex(
            $@"\bclass\s+(\w+DbContext)\s*\([^)]*\)\s*:\s*{Regex.Escape(baseTypeName)}<",
            RegexOptions.Compiled | RegexOptions.Singleline
        );
        var offenders = new List<string>();
        var inspected = 0;

        foreach (var file in SourceFiles.CSharpUnder(root, [.. options.SourceScanRoots]))
        {
            var stripped = SourceText.StripCommentsAndStrings(File.ReadAllText(file));
            var match = baseContextClass.Match(stripped);
            if (!match.Success)
                continue;

            inspected++;
            var contextName = match.Groups[1].Value;
            var companion = Path.Combine(Path.GetDirectoryName(file)!, $"I{contextName}.cs");
            if (!File.Exists(companion))
                offenders.Add($"{Rel(root, file)} (expected I{contextName}.cs alongside it)");
        }

        var message =
            "Every shared-base context needs a companion I{Name} interface in the same directory, "
            + "declaring its DbSets (and cross-schema reads). Offenders:\n  "
            + string.Join("\n  ", offenders);

        return new GuardResult(offenders, inspected, message);
    }

    /// <summary>
    /// Each domain context owns a distinct, non-null default schema (the schema half of 1:1:1). The
    /// model is built offline against PostgreSQL (no database connection) to read the applied schema.
    /// </summary>
    public static GuardResult OneSchemaPerContext(IReadOnlyList<Type> contextTypes)
    {
        ArgumentNullException.ThrowIfNull(contextTypes);

        var offenders = new List<string>();
        var schemas = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var contextType in contextTypes)
        {
            var schema = SchemaOf(contextType);
            if (string.IsNullOrEmpty(schema))
            {
                offenders.Add($"{contextType.Name} declares no default schema");
                continue;
            }

            (schemas.TryGetValue(schema, out var owners) ? owners : schemas[schema] = []).Add(
                contextType.Name
            );
        }

        foreach (var (schema, owners) in schemas.Where(kv => kv.Value.Count > 1))
            offenders.Add($"schema '{schema}' is shared by: {string.Join(", ", owners)}");

        var message =
            "Each domain context must own a distinct, non-null PostgreSQL schema (1:1:1). Give each "
            + "context its own Schema value. Offenders:\n  "
            + string.Join("\n  ", offenders);

        return new GuardResult(offenders, contextTypes.Count, message);
    }

    /// <summary>
    /// Each migration-based context's EF model must match its latest migration snapshot. A model edit
    /// committed without a matching migration makes <c>MigrateAsync</c> trip EF's
    /// <c>PendingModelChangesWarning</c> at startup, taking down every test that boots the host. Built
    /// offline against PostgreSQL (no database connection); the check compares the model to the
    /// snapshot in memory.
    /// </summary>
    /// <param name="migrationContextTypes">
    /// Only contexts that use migrations. A context bootstrapped with <c>EnsureSchemaCreatedAsync</c>
    /// has no snapshot, so every table reads as pending; do not pass those here.
    /// </param>
    public static GuardResult NoPendingModelChanges(IReadOnlyList<Type> migrationContextTypes)
    {
        ArgumentNullException.ThrowIfNull(migrationContextTypes);

        var offenders = new List<string>();
        foreach (var contextType in migrationContextTypes)
        {
            using var context = BuildOffline(contextType);
            if (context.Database.HasPendingModelChanges())
                offenders.Add(contextType.Name);
        }

        var message =
            "Each migration-based context's model must match its latest migration snapshot, or "
            + "MigrateAsync trips PendingModelChangesWarning at host startup. Run "
            + "`dotnet ef migrations add <Name>` to capture the change for each. Offenders:\n  "
            + string.Join("\n  ", offenders);

        return new GuardResult(offenders, migrationContextTypes.Count, message);
    }

    /// <summary>
    /// Every entity holding per-user data is scoped to its owner by a row filter that reads the
    /// principal, and, if it is exposed as a <c>[TraxQueryModel]</c>, carries a bare
    /// <c>[TraxAuthorize]</c>. Trax's own authorization checks see the attribute and nothing else,
    /// so an entity that is correctly gated but has no row filter passes them and still serves
    /// every user's rows to any authenticated caller. This is the check that sees the filter.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An entity is per-user when the model gives it an owner key (a foreign key to
    /// <see cref="OwnerScopeCensusOptions.OwnerType"/>, a property named in
    /// <see cref="OwnerScopeCensusOptions.OwnerIdProperties"/>, or it is the owner type itself), or
    /// when it carries a filter that reads the principal. Only a filter whose expression references
    /// <see cref="OwnerScopeCensusOptions.PrincipalAccessorType"/> counts: a soft-delete or
    /// visibility filter mentions no user, and treating it as ownership would pull a shared entity
    /// into the census and then fail it for being role-gated, which is right for a row that belongs
    /// to no one. EF declares filters on a hierarchy's root, so a derived type is judged by its
    /// root's filters. Owned types share their owner's table and filter and are skipped.
    /// </para>
    /// <para>
    /// Exposed per-user entities must be a bare <c>[TraxAuthorize]</c>.
    /// <c>[TraxAllowAnonymous]</c> hands one user's rows to anonymous callers, and a role or policy
    /// gate can lock owners out of their own rows: the filter is the access control.
    /// </para>
    /// <para>
    /// The model is the consumer's to build, because an owner-scoped context usually takes the
    /// principal accessor through its constructor. Building it needs no database connection.
    /// </para>
    /// </remarks>
    public static GuardResult OwnerScopeCompleteness(
        IReadOnlyModel model,
        OwnerScopeCensusOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.OwnerType);
        ArgumentNullException.ThrowIfNull(options.PrincipalAccessorType);

        var offenders = new List<string>();
        var perUser = new HashSet<Type>();
        var navigationOnly = new HashSet<Type>();
        var exposedAndGatedByRoleOrPolicy = new HashSet<Type>();
        var inspected = 0;

        var entities = model
            .GetEntityTypes()
            .Where(e => !e.IsOwned())
            .OrderBy(e => e.DisplayName(), StringComparer.Ordinal)
            .ToList();

        // A second mapping over a per-user table (a view or table mapping of the same name) reads
        // the same rows, so it is per-user whatever its own keys say. Without this, an unfiltered
        // alias of a filtered table would be invisible to the census.
        var perUserStorage = entities
            .Where(e =>
                HasDirectOwnerKey(e, options)
                || HasOwnerScopeFilter(e, options.PrincipalAccessorType)
            )
            .SelectMany(e => StorageOf(e).Select(storage => (storage, e)))
            .GroupBy(x => x.storage, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(x => x.e).ToList(), StringComparer.Ordinal);

        foreach (var entity in entities)
        {
            var clr = entity.ClrType;
            var name = entity.DisplayName();
            var sharedWith = StorageOf(entity)
                .SelectMany(storage =>
                    perUserStorage.TryGetValue(storage, out var owners)
                        ? owners
                            .Where(o => o.GetRootType() != entity.GetRootType())
                            .Select(o => $"{o.DisplayName()} ({storage})")
                        : []
                )
                .ToList();
            var hasOwnerKey = HasDirectOwnerKey(entity, options) || sharedWith.Count > 0;
            var hasOwnerFilter = HasOwnerScopeFilter(entity, options.PrincipalAccessorType);

            if (!hasOwnerKey && !hasOwnerFilter)
                continue;

            perUser.Add(clr);
            if (!hasOwnerKey)
                navigationOnly.Add(clr);

            if (options.Exemptions.ContainsKey(clr))
                continue;

            inspected++;

            if (hasOwnerKey && !hasOwnerFilter)
                offenders.Add(
                    $"{name}: holds per-user data ("
                        + (
                            sharedWith.Count > 0
                                ? "it reads the same storage as " + string.Join(", ", sharedWith)
                                : $"an owner key to {options.OwnerType.Name}"
                        )
                        + $") but has no HasQueryFilter that reads "
                        + $"{options.PrincipalAccessorType.Name}. Every caller who passes its "
                        + "authorization can read every owner's rows. Add the owner-scope filter, "
                        + "or exempt it with a reason."
                );

            if (clr.GetCustomAttribute<TraxQueryModelAttribute>(inherit: true) is null)
                continue;

            var posture = TraxAuthorization.Read(clr);
            var gates = posture
                .Authorize.Select(a =>
                    !string.IsNullOrWhiteSpace(a.Roles) ? $"Roles = \"{a.Roles}\""
                    : !string.IsNullOrWhiteSpace(a.Policy) ? $"Policy = \"{a.Policy}\""
                    : null
                )
                .OfType<string>()
                .ToArray();

            if (posture.AllowAnonymous)
                offenders.Add(
                    $"{name}: per-user [TraxQueryModel] marked [TraxAllowAnonymous], which "
                        + "exposes owners' rows to anonymous callers. Use a bare [TraxAuthorize]."
                );
            else if (!posture.HasAuthorize)
                offenders.Add(
                    $"{name}: per-user [TraxQueryModel] without [TraxAuthorize]. Add a bare "
                        + "[TraxAuthorize]; the row filter does the per-owner narrowing."
                );
            else if (gates.Length > 0 && options.Gated.ContainsKey(clr))
                exposedAndGatedByRoleOrPolicy.Add(clr);
            else if (gates.Length > 0)
                offenders.Add(
                    $"{name}: per-user [TraxQueryModel] gated by [TraxAuthorize("
                        + string.Join("), [TraxAuthorize(", gates)
                        + ")], which locks owners without it out of their own rows. Use a bare "
                        + "[TraxAuthorize]; the row filter is the access control. If the gate is "
                        + "deliberate, list it in Gated with a reason; the filter is still required."
                );
        }

        foreach (var (type, reason) in options.NavigationScoped.OrderBy(n => n.Key.FullName))
        {
            if (string.IsNullOrWhiteSpace(reason))
                offenders.Add(
                    $"{type.Name}: declared navigation-scoped with no reason. Say which "
                        + "navigation reaches its owner."
                );

            if (!navigationOnly.Contains(type) && !perUser.Contains(type))
                offenders.Add(
                    $"{type.Name}: declared navigation-scoped ({reason}) but has no row filter "
                        + $"that reads {options.PrincipalAccessorType.Name}, or is not in the "
                        + "model. With no owner key either, nothing else marks it as per-user: "
                        + "every authenticated caller now reads every owner's rows."
                );
            else if (!navigationOnly.Contains(type))
                offenders.Add(
                    $"{type.Name}: declared navigation-scoped but has an owner key of its own, so "
                        + "the direct check already covers it. Remove the declaration."
                );
        }

        foreach (var type in navigationOnly.OrderBy(t => t.FullName))
        {
            if (
                !options.NavigationScoped.ContainsKey(type) && !options.Exemptions.ContainsKey(type)
            )
                offenders.Add(
                    $"{type.Name}: has a row filter that reads "
                        + $"{options.PrincipalAccessorType.Name} but no owner key, and is not "
                        + "declared navigation-scoped. Declare it with the navigation it reaches "
                        + "its owner through, or deleting its filter later would silently "
                        + "un-scope it."
                );
        }

        foreach (var (type, reason) in options.Exemptions.OrderBy(e => e.Key.FullName))
        {
            if (string.IsNullOrWhiteSpace(reason))
                offenders.Add(
                    $"{type.Name}: exempted with no reason. Say why it is safe unfiltered."
                );

            if (!perUser.Contains(type))
                offenders.Add(
                    $"{type.Name}: exempted ({reason}) but the census does not see it as "
                        + "per-user, so the exemption covers nothing today and would silently "
                        + "cover whatever it becomes. Remove it."
                );
        }

        foreach (var (type, reason) in options.Gated.OrderBy(g => g.Key.FullName))
        {
            if (string.IsNullOrWhiteSpace(reason))
                offenders.Add(
                    $"{type.Name}: gated with no reason. Say who the role or policy is for."
                );

            if (options.Exemptions.ContainsKey(type))
                offenders.Add(
                    $"{type.Name}: both gated and exempted. An exemption skips the filter check, "
                        + "so the pair would let the gate stand in for the filter. A gate adds to "
                        + "the filter; remove the exemption."
                );
            else if (
                !exposedAndGatedByRoleOrPolicy.Contains(type)
                && !IsAnonymousOrUndeclaredExposure(type)
            )
                offenders.Add(
                    $"{type.Name}: gated ({reason}) but it is not a per-user [TraxQueryModel] "
                        + "whose [TraxAuthorize] names a role or policy, so the entry allows "
                        + "nothing today and would silently allow whatever it becomes. Remove it."
                );
            else if (!exposedAndGatedByRoleOrPolicy.Contains(type))
                offenders.Add(
                    $"{type.Name}: gated ({reason}), but a gate is added to [TraxAuthorize], never "
                        + "used in place of it. The posture reported for it still has to be fixed."
                );

            bool IsAnonymousOrUndeclaredExposure(Type t)
            {
                if (!perUser.Contains(t) || options.Exemptions.ContainsKey(t))
                    return false;
                if (t.GetCustomAttribute<TraxQueryModelAttribute>(inherit: true) is null)
                    return false;
                var posture = TraxAuthorization.Read(t);
                return posture.AllowAnonymous || !posture.HasAuthorize;
            }
        }

        var message =
            "Every entity holding per-user data must be scoped to its owner by a HasQueryFilter "
            + $"that reads {options.PrincipalAccessorType.Name}, and a per-user [TraxQueryModel] "
            + "must be a bare [TraxAuthorize]. Trax's authorization sees the attribute, not the "
            + "filter, so a missing filter passes every other check. Offenders:\n  "
            + string.Join("\n  ", offenders);

        return new GuardResult(offenders, inspected, message);
    }

    /// <summary>
    /// Query code does not switch an owner-scope filter off on a per-user set. The census
    /// (<see cref="OwnerScopeCompleteness"/>) reads the model, so a filter that is present there
    /// but disabled at a call site is invisible to it; this scans the source under
    /// <see cref="ArchitectureGuardOptions.SourceScanRoots"/> for the call sites.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two calls are found. <c>IgnoreQueryFilters()</c> with no arguments switches every filter
    /// off. An EF10 named-filter disable, <c>IgnoreQueryFilters(["Name"])</c>, is reported only
    /// when it names an owner-scope filter on a per-user entity (a named filter whose expression
    /// reads <see cref="OwnerScopeCensusOptions.PrincipalAccessorType"/>), or when its names are
    /// not string literals the scan can read.
    /// </para>
    /// <para>
    /// The set a call applies to is read from the text of its statement: a <c>DbSet</c> or
    /// <c>IQueryable</c> property declared under the scan roots, or <c>Set&lt;T&gt;()</c>. A call
    /// on a per-user set is reported, a call on a set of shared rows is not, and a call whose set
    /// the scan cannot name (a query passed in, a helper over <c>IQueryable&lt;T&gt;</c>) is
    /// reported, because it may be a per-user one. A file that switches the filter off on purpose
    /// goes in <see cref="OwnerScopeCensusOptions.FilterBypassAllowlist"/> with the reason.
    /// </para>
    /// <para>
    /// This is text, not a compiler: it does not follow a query built in one statement and
    /// filtered in another, and it cannot tell a <c>Notes</c> set on one context from one on
    /// another. It narrows where a leak can come from; cross-user behavioural tests, one caller
    /// trying to read another's rows, are what prove there is none.
    /// </para>
    /// </remarks>
    public static GuardResult OwnerScopeFilterBypasses(
        ArchitectureGuardOptions scan,
        IReadOnlyList<IReadOnlyModel> models,
        OwnerScopeCensusOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.PrincipalAccessorType);

        // Entities whose root carries an owner-scope filter: the ones a bypass exposes. Keyed by
        // simple name, because that is all the source text gives.
        var scopedEntities = models
            .SelectMany(m => m.GetEntityTypes())
            .Where(e => !e.IsOwned() && HasOwnerScopeFilter(e, options.PrincipalAccessorType))
            .ToList();
        var perUserTypeNames = scopedEntities
            .Select(e => e.ClrType.Name)
            .ToHashSet(StringComparer.Ordinal);
        var ownerFilterNames = scopedEntities
            .SelectMany(e => e.GetRootType().GetDeclaredQueryFilters())
            .Where(f =>
                !f.IsAnonymous
                && f.Expression is not null
                && References(f.Expression, options.PrincipalAccessorType)
            )
            .Select(f => f.Key!)
            .ToHashSet(StringComparer.Ordinal);

        var root = scan.RepoRootOverride ?? RepoRoot.Path;
        var files = SourceFiles
            .CSharpUnder(root, [.. scan.SourceScanRoots])
            .Select(file => (Path: Rel(root, file), Text: File.ReadAllText(file)))
            .Select(f => (f.Path, f.Text, Stripped: SourceText.StripCommentsAndStrings(f.Text)))
            .ToList();

        // Set name -> element type names, from every DbSet/IQueryable property under the roots.
        var sets = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var file in files)
        foreach (Match declaration in SetDeclaration.Matches(file.Stripped))
        {
            var name = declaration.Groups["name"].Value;
            if (!sets.TryGetValue(name, out var types))
                sets[name] = types = new HashSet<string>(StringComparer.Ordinal);
            types.Add(declaration.Groups["type"].Value);
        }

        var offenders = new List<string>();
        var bypassingFiles = new HashSet<string>(StringComparer.Ordinal);
        var inspected = 0;

        foreach (var file in files)
        foreach (Match call in IgnoreQueryFiltersCall.Matches(file.Stripped))
        {
            inspected++;
            var line = LineOf(file.Stripped, call.Index);
            var close = MatchingParen(file.Stripped, call.Index + call.Length - 1);
            var arguments = file.Text[(call.Index + call.Length)..close].Trim();

            string? disables = null;
            if (arguments.Length > 0)
            {
                var names = LiteralNames(arguments);
                if (names is null)
                    disables = "filters named by an expression the scan cannot read";
                else if (names.FirstOrDefault(ownerFilterNames.Contains) is { } owner)
                    disables = $"the owner-scope filter \"{owner}\"";
                else
                    continue;
            }

            var statement = StatementBefore(file.Stripped, call.Index);
            var perUser = new SortedSet<string>(StringComparer.Ordinal);
            var shared = false;
            foreach (Match reference in SetReference.Matches(statement))
            {
                var typed = reference.Groups["type"];
                var element = typed.Success ? [typed.Value] : ElementTypes(reference.Value);
                foreach (var type in element)
                    if (perUserTypeNames.Contains(type))
                        perUser.Add(type);
                    else
                        shared = true;
            }

            IEnumerable<string> ElementTypes(string name) =>
                sets.TryGetValue(name, out var types) ? types : [];

            if (perUser.Count == 0 && shared)
                continue;

            bypassingFiles.Add(file.Path);
            if (options.FilterBypassAllowlist.ContainsKey(file.Path))
                continue;

            var what = disables ?? "every query filter, the owner scope included";
            offenders.Add(
                perUser.Count > 0
                    ? $"{file.Path}:{line}: IgnoreQueryFilters switches off {what} on "
                        + $"{string.Join(", ", perUser)}, which holds per-user data. Every caller "
                        + "reaching this query reads every owner's rows."
                    : $"{file.Path}:{line}: IgnoreQueryFilters switches off {what}, and the scan "
                        + "cannot tell which set it applies to, so it may be a per-user one."
            );
        }

        foreach (var (path, reason) in options.FilterBypassAllowlist.OrderBy(a => a.Key))
        {
            if (string.IsNullOrWhiteSpace(reason))
                offenders.Add(
                    $"{path}: allowlisted to switch an owner-scope filter off with no reason. Say "
                        + "why reading every owner's rows is right there."
                );
            else if (!bypassingFiles.Contains(path))
                offenders.Add(
                    $"{path}: allowlisted ({reason}) but switches no owner-scope filter off, so "
                        + "the entry covers nothing today and would silently cover whatever the "
                        + "file does next. Remove it."
                );
        }

        var message =
            "Query code must not switch an owner-scope filter off on a per-user set: the census "
            + "sees the filter in the model and cannot see it disabled at a call site. Keep the "
            + "filter, or allowlist the file in FilterBypassAllowlist with the reason "
            + "(docs/adr/0008-per-user-data-is-filtered-by-its-owner.md). Offenders:\n  "
            + string.Join("\n  ", offenders);

        return new GuardResult(offenders, inspected, message);
    }

    private static readonly Regex IgnoreQueryFiltersCall = new(
        @"\.\s*IgnoreQueryFilters\s*\(",
        RegexOptions.Compiled
    );

    private static readonly Regex SetDeclaration = new(
        @"\b(?:DbSet|IQueryable)\s*<\s*(?:[\w.]+\.)?(?<type>\w+)\s*>\s+(?<name>\w+)\s*(?:\{|=>|=|;)",
        RegexOptions.Compiled
    );

    // Set<Note>() names its type; any other identifier might be a set property, and is looked up.
    private static readonly Regex SetReference = new(
        @"\bSet\s*<\s*(?:[\w.]+\.)?(?<type>\w+)\s*>|\b\w+\b",
        RegexOptions.Compiled
    );

    private static readonly Regex StringLiteral = new(
        @"""((?:[^""\\]|\\.)*)""",
        RegexOptions.Compiled
    );

    private static readonly Regex LiteralCollection = new(
        @"^(?:new\s*(?:string)?\s*\[\s*\]\s*\{|\[)?\s*(?:""(?:[^""\\]|\\.)*""\s*,?\s*)*(?:\}|\])?$",
        RegexOptions.Compiled
    );

    /// <summary>
    /// The filter names in a named-filter disable, or <c>null</c> when they are not all string
    /// literals in a collection expression or array initializer.
    /// </summary>
    private static IReadOnlyList<string>? LiteralNames(string arguments) =>
        LiteralCollection.IsMatch(arguments)
            ? StringLiteral.Matches(arguments).Select(m => m.Groups[1].Value).ToList()
            : null;

    /// <summary>
    /// The text of the statement a call belongs to, up to the call: back to the previous
    /// <c>;</c>, <c>{</c> or <c>}</c>. A block-bodied lambda earlier in the chain cuts it short,
    /// which can only leave the set unnamed, and an unnamed set is reported.
    /// </summary>
    private static string StatementBefore(string stripped, int index)
    {
        var start = stripped.LastIndexOfAny([';', '{', '}'], Math.Max(0, index - 1));
        return stripped[(start + 1)..index];
    }

    private static int MatchingParen(string stripped, int open)
    {
        var depth = 0;
        for (var i = open; i < stripped.Length; i++)
        {
            if (stripped[i] == '(')
                depth++;
            else if (stripped[i] == ')' && --depth == 0)
                return i;
        }
        return stripped.Length;
    }

    private static int LineOf(string text, int index) => 1 + text.AsSpan(0, index).Count('\n');

    /// <summary>
    /// The tables and views an entity reads, schema-qualified. Two entity types naming the same one
    /// read the same rows.
    /// </summary>
    private static IEnumerable<string> StorageOf(IReadOnlyEntityType entity)
    {
        if (entity.GetTableName() is { } table)
            yield return $"{entity.GetSchema()}.{table}";
        if (entity.GetViewName() is { } view)
            yield return $"{entity.GetViewSchema() ?? entity.GetSchema()}.{view}";
    }

    private static bool HasDirectOwnerKey(
        IReadOnlyEntityType entity,
        OwnerScopeCensusOptions options
    ) =>
        options.OwnerType.IsAssignableFrom(entity.ClrType)
        || entity
            .GetForeignKeys()
            .Any(fk => options.OwnerType.IsAssignableFrom(fk.PrincipalEntityType.ClrType))
        || entity.GetProperties().Any(p => options.OwnerIdProperties.Contains(p.Name));

    private static bool HasOwnerScopeFilter(IReadOnlyEntityType entity, Type accessorType) =>
        entity
            .GetRootType()
            .GetDeclaredQueryFilters()
            .Any(f => f.Expression is not null && References(f.Expression, accessorType));

    private static bool References(Expression expression, Type accessorType)
    {
        var finder = new TypeReferenceFinder(accessorType);
        finder.Visit(expression);
        return finder.Found;
    }

    /// <summary>
    /// Finds any node typed as the accessor. The accessor is usually captured as a member of the
    /// context, so it surfaces as a node's type rather than as a parameter, and matching on the
    /// type keeps working when the member is renamed or the predicate rearranged.
    /// </summary>
    private sealed class TypeReferenceFinder(Type accessorType) : ExpressionVisitor
    {
        public bool Found { get; private set; }

        public override Expression? Visit(Expression? node)
        {
            if (Found || node is null)
                return node;

            if (accessorType.IsAssignableFrom(node.Type))
            {
                Found = true;
                return node;
            }

            return base.Visit(node);
        }
    }

    private static string? SchemaOf(Type contextType)
    {
        using var context = BuildOffline(contextType);
        return context.Model.GetDefaultSchema();
    }

    /// <summary>
    /// Builds a context instance offline (PostgreSQL provider, no connection) via its
    /// <c>DbContextOptions</c> constructor, for reflection-only model inspection.
    /// </summary>
    private static DbContext BuildOffline(Type contextType)
    {
        var builderType = typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType);
        var builder = (DbContextOptionsBuilder)Activator.CreateInstance(builderType)!;
        builder.UseNpgsql("Host=localhost;Database=offline_model_only");

        // DbContextOptionsBuilder<T> shadows the base Options property (DbContextOptions<T> vs the
        // non-generic DbContextOptions), so restrict to the one declared on the generic builder.
        var options = builderType
            .GetProperty(
                "Options",
                System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.DeclaredOnly
            )!
            .GetValue(builder);
        return (DbContext)Activator.CreateInstance(contextType, options)!;
    }

    private static string Rel(string root, string file) =>
        Path.GetRelativePath(root, file).Replace('\\', '/');
}
