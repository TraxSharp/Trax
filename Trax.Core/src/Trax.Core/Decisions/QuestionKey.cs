using System.Reflection;
using Trax.Core.Utils;

namespace Trax.Core.Decisions;

/// <summary>
/// The key a question is asked under: the <see cref="AsksAttribute.Key"/> on the type it is about,
/// or else the type's name without its namespace.
/// </summary>
/// <remarks>
/// A decider finds each answer's question by <see cref="Question.Key"/>, a decision model's adapter
/// sends it as the question's id (which a model such as Nimble may read), a replay looks answers up
/// by it, and it is part of the fingerprint of every asking. So it is short, and it stays the same
/// when the type moves to another namespace.
///
/// <para>Without a <see cref="AsksAttribute.Key"/>, it is the type's name, after the name of every
/// type it is nested in, joined by dots, with any generic arguments in angle brackets, separated by
/// commas and written the same way: <c>Priority</c>, <c>Queue.Lane</c> for an enum nested in a
/// class, <c>Flag&lt;Refund&gt;</c> for a generic marker. Renaming the type, or a type it is nested
/// in, changes the key, so a run after the rename asks afresh rather than replaying an answer
/// recorded under the old one. Setting <see cref="AsksAttribute.Key"/> to the old key keeps it.</para>
///
/// <para>Two types in one train whose keys are the same are a declaration the startup check
/// refuses, because their answers could not be told apart. Setting
/// <see cref="AsksAttribute.Key"/> on one of them gives it a key of its own.</para>
/// </remarks>
public static class QuestionKey
{
    /// <summary>The longest key <see cref="AsksAttribute.Key"/> may set.</summary>
    public const int MaxLength = 100;

    /// <summary>The key of the question about <typeparamref name="T"/>.</summary>
    public static string For<T>() => For(typeof(T));

    /// <summary>The key of the question about <paramref name="type"/>.</summary>
    public static string For(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return Declared(type) ?? Name(type, type.IsGenericType ? type.GetGenericArguments() : []);
    }

    /// <summary>
    /// Whether <paramref name="key"/> can be set as <see cref="AsksAttribute.Key"/>: from 1 to
    /// <see cref="MaxLength"/> characters, each an ASCII letter or digit, <c>_</c>, <c>-</c> or
    /// <c>.</c>, so it can be sent as a JSON property name, written in a log field or put in a
    /// model's prompt as it is.
    /// </summary>
    public static bool IsValid(string? key) =>
        key is { Length: > 0 and <= MaxLength }
        && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.');

    /// <summary>What is wrong with the key <paramref name="type"/> declares, or null.</summary>
    internal static string? Problem(Type type) =>
        Declared(type) is { } key && !IsValid(key)
            ? $"declares the question key '{key}' on '{Name(type, type.IsGenericType ? type.GetGenericArguments() : [])}'. "
                + $"A key is 1 to {MaxLength} ASCII letters, digits, '_', '-' or '.'."
            : null;

    /// <summary>
    /// The problem with two different types whose questions one train asks under one key.
    /// </summary>
    internal static string Shared(string key, Type first, Type second) =>
        $"asks about '{first.ReadableName()}' and '{second.ReadableName()}' under the same "
        + $"question key '{key}', so their answers cannot be told apart. Give one of them a key "
        + "of its own with [Asks(..., Key = \"...\")].";

    private static string? Declared(Type type) =>
        type.IsGenericParameter
            ? null
            : type.GetCustomAttribute<AsksAttribute>(inherit: false)?.Key;

    private static string Name(Type type, Type[] arguments)
    {
        if (type.IsArray)
            return For(type.GetElementType()!) + "[]";

        if (type.IsGenericParameter)
            return type.Name;

        var prefix = "";
        var inherited = 0;

        // A type nested in a generic type takes the outer type's arguments ahead of its own.
        if (type.IsNested && type.DeclaringType is { } outer)
        {
            if (outer.IsGenericType)
                inherited = Math.Min(outer.GetGenericArguments().Length, arguments.Length);

            prefix = Name(outer, arguments[..inherited]) + ".";
        }

        var name = type.Name;
        var tick = name.IndexOf('`');

        if (tick >= 0)
            name = name[..tick];

        var own = arguments[inherited..];

        return own.Length == 0
            ? prefix + name
            : $"{prefix}{name}<{string.Join(",", own.Select(For))}>";
    }
}
