using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Trax.Effect.Attributes;
using Trax.Effect.Services.JunctionEvents;

namespace Trax.Effect.Utils;

/// <summary>
/// Serializer options that write a <see cref="TraxSensitiveAttribute"/> member as
/// <c>{"_redacted": true}</c>, for every copy of a train's input or output that Trax keeps, and
/// the check of whether a recorded decision's answer must be withheld.
/// </summary>
public static class TraxRedaction
{
    /// <summary>
    /// The property of the object that stands in for a masked value.
    /// </summary>
    public const string MarkerProperty = "_redacted";

    private static readonly ConditionalWeakTable<
        JsonSerializerOptions,
        JsonSerializerOptions
    > Derived = new();

    /// <summary>
    /// Returns options that serialize exactly as <paramref name="options"/> do, except that every
    /// member marked <see cref="TraxSensitiveAttribute"/> is written as <c>{"_redacted": true}</c>.
    /// </summary>
    /// <remarks>
    /// The same instance is returned for the same <paramref name="options"/>, so System.Text.Json's
    /// per-options metadata cache survives across calls. <paramref name="options"/> itself is not
    /// changed. The returned options are for writing: reading a masked value back throws
    /// <see cref="JsonException"/>, because the mask is not the value.
    /// </remarks>
    public static JsonSerializerOptions WithRedaction(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return Derived.GetValue(options, Derive);
    }

    /// <summary>
    /// Whether <paramref name="json"/> contains a masked value anywhere, which means it cannot be
    /// read back as the input or output it was written from. Text that is not JSON has none.
    /// </summary>
    public static bool ContainsRedaction(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (!json.Contains(MarkerProperty, StringComparison.Ordinal))
            return false;

        try
        {
            using var document = JsonDocument.Parse(json);
            return ContainsMarker(document.RootElement);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Whether the answer to the question with this key must be withheld wherever a recorded
    /// decision is read back: the question is about an enum or marker type marked
    /// <see cref="TraxSensitiveAttribute"/>, or the key is built from the name of one.
    /// </summary>
    /// <param name="questionKey">
    /// The question's key as Trax records it, such as <c>trax.decision.question_key</c>.
    /// </param>
    /// <remarks>
    /// <para>The rule junction events, <c>trax.junction_run</c> and the decision journal apply,
    /// read from the key alone, since a stored decision carries no type. Each marked type in a
    /// loaded assembly that references Trax.Effect contributes its <c>[Asks(Key = ...)]</c> when it
    /// declares one and its name without namespace or generic arity, and a key is sensitive when it
    /// is one of those names or is built from one (<c>Flag&lt;Refund&gt;</c>,
    /// <c>Outer&lt;X&gt;.Flag</c>, <c>Flag[]</c>).</para>
    /// <para>It fails closed: a key drops the namespace, so two types can share a name, and when
    /// either is marked the answer is withheld for both. A marked type must be in an assembly this
    /// process has loaded for its keys to be recognised.</para>
    /// </remarks>
    public static bool IsSensitiveQuestion(string questionKey)
    {
        ArgumentNullException.ThrowIfNull(questionKey);
        return SensitiveQuestions.IsSensitive(questionKey);
    }

    private static bool ContainsMarker(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (
                        property.NameEquals(MarkerProperty)
                        && property.Value.ValueKind == JsonValueKind.True
                    )
                        return true;
                    if (ContainsMarker(property.Value))
                        return true;
                }
                return false;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    if (ContainsMarker(item))
                        return true;
                return false;
            default:
                return false;
        }
    }

    private static JsonSerializerOptions Derive(JsonSerializerOptions options)
    {
        var resolver = options.TypeInfoResolver ?? new DefaultJsonTypeInfoResolver();
        var derived = new JsonSerializerOptions(options)
        {
            TypeInfoResolver = resolver.WithAddedModifier(MaskSensitiveMembers),
        };
        derived.MakeReadOnly();
        return derived;
    }

    private static void MaskSensitiveMembers(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
            return;

        foreach (var property in typeInfo.Properties)
            if (IsSensitive(typeInfo.Type, property))
                property.CustomConverter = (JsonConverter)
                    Activator.CreateInstance(
                        typeof(RedactingConverter<>).MakeGenericType(property.PropertyType)
                    )!;
    }

    /// <summary>
    /// Marked on the member itself (or a base declaration of it), on the constructor parameter it
    /// binds to (a positional record's parameter), or on an interface member it implements.
    /// </summary>
    private static bool IsSensitive(Type declaringType, JsonPropertyInfo property)
    {
        if (
            property.AttributeProvider is MemberInfo member
            && Attribute.IsDefined(member, typeof(TraxSensitiveAttribute), inherit: true)
        )
            return true;

        if (
            property.AssociatedParameter?.AttributeProvider is ParameterInfo parameter
            && parameter.IsDefined(typeof(TraxSensitiveAttribute), inherit: false)
        )
            return true;

        if (property.AttributeProvider is not MemberInfo clrMember)
            return false;

        // A record's primary constructor parameter, when System.Text.Json did not bind to that
        // constructor (another one was chosen, or the type is only ever written).
        foreach (var constructor in declaringType.GetConstructors())
        foreach (var candidate in constructor.GetParameters())
            if (
                string.Equals(candidate.Name, clrMember.Name, StringComparison.Ordinal)
                && candidate.IsDefined(typeof(TraxSensitiveAttribute), inherit: false)
            )
                return true;

        foreach (var contract in declaringType.GetInterfaces())
        {
            var declared = contract.GetProperty(clrMember.Name);
            if (declared is not null && declared.IsDefined(typeof(TraxSensitiveAttribute), false))
                return true;
        }

        return false;
    }

    private static readonly string EffectAssembly = typeof(TraxSensitiveAttribute)
        .Assembly.GetName()
        .Name!;

    private const BindingFlags InstanceMembers =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>
    /// The loaded types a value held as a base type, an interface or <see cref="object"/> can be,
    /// and what has been worked out from them. Dropped when an assembly that could add to them
    /// loads.
    /// </summary>
    private sealed class Loaded(Type[] types, HashSet<string> assemblies)
    {
        public Type[] Types { get; } = types;

        public HashSet<string> Assemblies { get; } = assemblies;

        public ConcurrentDictionary<Type, bool> Reaches { get; } = new();
    }

    private static Loaded? _loaded;

    private static int _watching;

    /// <summary>
    /// Whether a value of <paramref name="type"/> can hold a value marked
    /// <see cref="TraxSensitiveAttribute"/>: the type itself is marked, or any instance member,
    /// public or not, of it or of a type it holds (a member's type, an element type, a generic
    /// argument), recursively, is marked as the masking finds a mark (on the member, the record
    /// parameter it binds to, or an interface member it implements).
    /// </summary>
    /// <remarks>
    /// A member typed <see cref="object"/>, an interface, or a class that is not sealed can hold
    /// any type assignable to it, so it reaches a mark when any loaded type assignable to it does.
    /// Those types are looked for in the loaded assemblies that reference Trax.Effect, directly or
    /// through another assembly, since only they can hold a type that carries the mark, and the
    /// answers are worked out again when another such assembly loads. A generic parameter is
    /// treated as <see cref="object"/>. Types from framework assemblies are not looked into, only
    /// their generic arguments, element types, and what a collection among them holds.
    /// </remarks>
    internal static bool ReachesSensitiveMember(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var loaded = LoadedTypes();
        return loaded.Reaches.GetOrAdd(type, t => Reach(t, [], loaded.Types));
    }

    private static Loaded LoadedTypes()
    {
        if (Volatile.Read(ref _loaded) is { } current)
            return current;

        if (Interlocked.Exchange(ref _watching, 1) == 0)
            AppDomain.CurrentDomain.AssemblyLoad += (_, args) => OnLoad(args.LoadedAssembly);

        var built = Scan();
        return Interlocked.CompareExchange(ref _loaded, built, null) ?? built;
    }

    /// <summary>Drops what was worked out when an assembly that can hold a marked type loads.</summary>
    private static void OnLoad(Assembly assembly)
    {
        try
        {
            if (Volatile.Read(ref _loaded) is not { } current)
                return;

            if (assembly.IsDynamic || IsFramework(assembly))
                return;

            if (
                assembly.GetName().Name == EffectAssembly
                || assembly
                    .GetReferencedAssemblies()
                    .Any(r =>
                        r.Name == EffectAssembly
                        || r.Name is { } name && current.Assemblies.Contains(name)
                    )
            )
                Interlocked.CompareExchange(ref _loaded, null, current);
        }
        catch
        {
            // An assembly whose references cannot be read: drop what was worked out, to be safe.
            Volatile.Write(ref _loaded, null);
        }
    }

    private static Loaded Scan()
    {
        var assemblies = AppDomain
            .CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !IsFramework(a))
            .ToList();

        var references = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var assembly in assemblies)
        {
            try
            {
                var name = assembly.GetName().Name;
                if (name is null)
                    continue;
                if (!references.TryGetValue(name, out var list))
                    references[name] = list = [];
                list.AddRange(
                    assembly.GetReferencedAssemblies().Select(r => r.Name).OfType<string>()
                );
            }
            catch
            {
                // An assembly whose references cannot be read cannot be told apart; it is
                // looked into below as if it referenced Trax.Effect.
                if (assembly.GetName().Name is { } name)
                    references[name] = [EffectAssembly];
            }
        }

        var reaching = new Dictionary<string, bool>(StringComparer.Ordinal);

        bool ReachesEffect(string name)
        {
            if (name == EffectAssembly)
                return true;
            if (reaching.TryGetValue(name, out var known))
                return known;
            if (!references.TryGetValue(name, out var referenced))
                return false;

            reaching[name] = false;
            var reaches = referenced.Any(ReachesEffect);
            reaching[name] = reaches;
            return reaches;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var types = new List<Type>();

        foreach (var assembly in assemblies)
        {
            if (assembly.GetName().Name is not { } name || !ReachesEffect(name))
                continue;

            names.Add(name);

            Type?[] declared;
            try
            {
                declared = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException partial)
            {
                declared = partial.Types;
            }
            catch
            {
                continue;
            }

            types.AddRange(declared.OfType<Type>().Where(t => !t.IsInterface));
        }

        return new Loaded([.. types], names);
    }

    private static bool Reach(Type type, HashSet<Type> seen, Type[] loaded)
    {
        if (!seen.Add(type))
            return false;

        // A generic parameter can be bound to any type.
        if (type.IsGenericParameter)
            return Reach(typeof(object), seen, loaded);

        if (type.IsDefined(typeof(TraxSensitiveAttribute), inherit: true))
            return true;

        if (
            type.HasElementType
            && type.GetElementType() is { } element
            && Reach(element, seen, loaded)
        )
            return true;

        if (type.IsGenericType && type.GetGenericArguments().Any(a => Reach(a, seen, loaded)))
            return true;

        if (type.IsPrimitive || type.IsEnum || type.IsPointer || type == typeof(string))
            return false;

        // A member typed object, an interface or a class that is not sealed holds whatever is
        // assignable to it, so it reaches a mark when any loaded type it can hold does.
        if (
            (type.IsInterface || (type.IsClass && !type.IsSealed))
            && loaded.Any(t => t != type && CanHold(type, t, seen, loaded))
        )
            return true;

        if (IsFramework(type.Assembly))
            return HeldByCollection(type, seen, loaded);

        for (
            var current = type;
            current is not null && current != typeof(object);
            current = current.BaseType
        )
        {
            var parameters = current
                .GetConstructors(InstanceMembers)
                .SelectMany(c => c.GetParameters())
                .Where(p => p.IsDefined(typeof(TraxSensitiveAttribute), inherit: false))
                .Select(p => p.Name)
                .ToHashSet(StringComparer.Ordinal);

            foreach (
                var property in current.GetProperties(InstanceMembers | BindingFlags.DeclaredOnly)
            )
                if (
                    property.IsDefined(typeof(TraxSensitiveAttribute), inherit: true)
                    || parameters.Contains(property.Name)
                    || current
                        .GetInterfaces()
                        .Select(i => i.GetProperty(property.Name))
                        .Any(p => p?.IsDefined(typeof(TraxSensitiveAttribute), false) == true)
                )
                    return true;

            foreach (var field in current.GetFields(InstanceMembers | BindingFlags.DeclaredOnly))
                if (
                    field.IsDefined(typeof(TraxSensitiveAttribute), inherit: false)
                    || Reach(field.FieldType, seen, loaded)
                )
                    return true;
        }

        return false;
    }

    /// <summary>
    /// Whether a member typed <paramref name="member"/> can hold a <paramref name="candidate"/> that
    /// reaches a mark. A type that cannot be inspected is taken to reach one.
    /// </summary>
    private static bool CanHold(Type member, Type candidate, HashSet<Type> seen, Type[] loaded)
    {
        try
        {
            return member.IsAssignableFrom(candidate) && Reach(candidate, seen, loaded);
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// What a framework collection holds: the items of each <see cref="IEnumerable{T}"/> it is,
    /// or any value for one that is only <see cref="System.Collections.IEnumerable"/>.
    /// </summary>
    private static bool HeldByCollection(Type type, HashSet<Type> seen, Type[] loaded)
    {
        if (!typeof(System.Collections.IEnumerable).IsAssignableFrom(type))
            return false;

        var items = type.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            .Select(i => i.GetGenericArguments()[0])
            .ToList();

        return items.Count == 0
            ? Reach(typeof(object), seen, loaded)
            : items.Any(item => Reach(item, seen, loaded));
    }

    /// <summary>
    /// Whether <paramref name="assembly"/> is part of .NET itself, decided as Trax.Core's state
    /// hash decides it, so a consumer's own type is looked into whatever its namespace is.
    /// </summary>
    private static bool IsFramework(Assembly assembly) =>
        assembly == typeof(object).Assembly
        || assembly.GetName().Name is { } name
            && (
                name.StartsWith("System.", StringComparison.Ordinal)
                || name == "System"
                || name == "netstandard"
            );

    /// <summary>
    /// Writes the stand-in object whatever the value is, and refuses to read one back.
    /// </summary>
    private sealed class RedactingConverter<T> : JsonConverter<T>
    {
        public override T Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        ) =>
            throw new JsonException(
                "A value masked by [TraxSensitive] cannot be read back; the stored copy does not "
                    + "hold it."
            );

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteBoolean(MarkerProperty, true);
            writer.WriteEndObject();
        }
    }
}
