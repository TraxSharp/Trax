# Trax.Analyzers.Annotations

`Trax.Analyzers.Annotations` enables you to configure certain analyzer rules by adding annotations directly to your code.

`Trax.Analyzers.Annotations` is a separate dependency from `Trax.Analyzers`. If you want to use these attributes in your code, add an explicit package reference:

```bash
dotnet add package Trax.Analyzers.Annotations
```

You can also copy the attribute source files into your project as long as the namespace and type names match. The copied types can be `public` or `internal`.

By default, all usages of attributes from `Trax.Analyzers.Annotations` are removed from the compiled assembly metadata. This means your binaries will not reference the `Trax.Analyzers.Annotations.dll` assembly.

If you want to keep these attributes in the metadata (for example, for reflection or tooling purposes), define the `TRAX_ANALYZERS_ANNOTATIONS` conditional compilation symbol in your project settings.

## Available attributes

| Attribute | Purpose | Related rules |
| --- | --- | --- |
| `DoNotIgnoreAttribute` | Marks a return value or `out` parameter as must-not-be-ignored. | [TRX0060](https://github.com/TraxSharp/Trax/blob/main/Trax.Analyzers/docs/rules/TRX0060.md) |
| `CultureInsensitiveTypeAttribute` | Marks a type (or a specific format) as culture-insensitive. | [TRX0011](https://github.com/TraxSharp/Trax/blob/main/Trax.Analyzers/docs/rules/TRX0011.md), [TRX0075](https://github.com/TraxSharp/Trax/blob/main/Trax.Analyzers/docs/rules/TRX0075.md), [TRX0076](https://github.com/TraxSharp/Trax/blob/main/Trax.Analyzers/docs/rules/TRX0076.md), [TRX0185](https://github.com/TraxSharp/Trax/blob/main/Trax.Analyzers/docs/rules/TRX0185.md) |
| `CultureInsensitiveAttribute` | Marks the value of a property, a field, or a parameter as culture-insensitive. | [TRX0011](https://github.com/TraxSharp/Trax/blob/main/Trax.Analyzers/docs/rules/TRX0011.md), [TRX0075](https://github.com/TraxSharp/Trax/blob/main/Trax.Analyzers/docs/rules/TRX0075.md), [TRX0076](https://github.com/TraxSharp/Trax/blob/main/Trax.Analyzers/docs/rules/TRX0076.md), [TRX0185](https://github.com/TraxSharp/Trax/blob/main/Trax.Analyzers/docs/rules/TRX0185.md) |
| `NonAwaitableTypeAttribute` | Excludes await recommendations for specific types. | [TRX0042](https://github.com/TraxSharp/Trax/blob/main/Trax.Analyzers/docs/rules/TRX0042.md), [TRX0045](https://github.com/TraxSharp/Trax/blob/main/Trax.Analyzers/docs/rules/TRX0045.md), [TRX0134](https://github.com/TraxSharp/Trax/blob/main/Trax.Analyzers/docs/rules/TRX0134.md), [TRX0137](https://github.com/TraxSharp/Trax/blob/main/Trax.Analyzers/docs/rules/TRX0137.md), [TRX0138](https://github.com/TraxSharp/Trax/blob/main/Trax.Analyzers/docs/rules/TRX0138.md) |
| `NonAsyncDisposableTypeAttribute` | Excludes `await using` recommendations for specific types. | [TRX0042](https://github.com/TraxSharp/Trax/blob/main/Trax.Analyzers/docs/rules/TRX0042.md), [TRX0045](https://github.com/TraxSharp/Trax/blob/main/Trax.Analyzers/docs/rules/TRX0045.md) |
| `ExcludeFromBlockingCallAnalysisAttribute` | Excludes specific methods/properties from blocking-call diagnostics. | [TRX0042](https://github.com/TraxSharp/Trax/blob/main/Trax.Analyzers/docs/rules/TRX0042.md), [TRX0045](https://github.com/TraxSharp/Trax/blob/main/Trax.Analyzers/docs/rules/TRX0045.md) |
| `ExcludeFromCancellationTokenAnalysisAttribute` | Excludes specific methods from the `CancellationToken` diagnostics. | [TRX0032](https://github.com/TraxSharp/Trax/blob/main/Trax.Analyzers/docs/rules/TRX0032.md), [TRX0040](https://github.com/TraxSharp/Trax/blob/main/Trax.Analyzers/docs/rules/TRX0040.md), [TRX0079](https://github.com/TraxSharp/Trax/blob/main/Trax.Analyzers/docs/rules/TRX0079.md), [TRX0080](https://github.com/TraxSharp/Trax/blob/main/Trax.Analyzers/docs/rules/TRX0080.md) |
| `RequireNamedArgumentAttribute` | Requires named arguments for decorated parameters. | [TRX0003](https://github.com/TraxSharp/Trax/blob/main/Trax.Analyzers/docs/rules/TRX0003.md) |
| `StructuredLogFieldAttribute` | Declares allowed types for named log properties in an assembly. | [TRX0124](https://github.com/TraxSharp/Trax/blob/main/Trax.Analyzers/docs/rules/TRX0124.md), [TRX0139](https://github.com/TraxSharp/Trax/blob/main/Trax.Analyzers/docs/rules/TRX0139.md) |

## ExcludeFromBlockingCallAnalysisAttribute

Use `ExcludeFromBlockingCallAnalysisAttribute` to exclude specific TRX0042/TRX0045 method/property diagnostics at the assembly level.

```csharp
[assembly: Trax.Analyzers.Annotations.ExcludeFromBlockingCallAnalysisAttribute("M:System.Threading.Tasks.Task.Wait")]
[assembly: Trax.Analyzers.Annotations.ExcludeFromBlockingCallAnalysisAttribute(typeof(System.Threading.Thread), "Sleep", typeof(int))]
```

## ExcludeFromCancellationTokenAnalysisAttribute

Use `ExcludeFromCancellationTokenAnalysisAttribute` to mark a method as valid to call without a `CancellationToken`. No TRX0032/TRX0040/TRX0079/TRX0080 diagnostic is reported for the calls to an excluded method, even when the method has an overload with a `CancellationToken` and a token is available in the scope.

Methods of the current project can be annotated directly:

```csharp
class Sample
{
    [Trax.Analyzers.Annotations.ExcludeFromCancellationTokenAnalysis]
    public static System.Threading.Tasks.Task FlushAsync() => throw null;
    public static System.Threading.Tasks.Task FlushAsync(System.Threading.CancellationToken cancellationToken) => throw null;
}

await Sample.FlushAsync(); // No TRX0040 diagnostic
```

Methods of another assembly can be annotated at the assembly level, using their XML documentation id or their containing type and name. The parameter types are optional and restrict the exclusion to a single overload.

```csharp
[assembly: Trax.Analyzers.Annotations.ExcludeFromCancellationTokenAnalysis("M:System.Threading.Channels.ChannelWriter`1.WriteAsync(`0,System.Threading.CancellationToken)")]
[assembly: Trax.Analyzers.Annotations.ExcludeFromCancellationTokenAnalysis(typeof(Sample), "FlushAsync")]
[assembly: Trax.Analyzers.Annotations.ExcludeFromCancellationTokenAnalysis(typeof(Sample), "FlushAsync", typeof(System.Threading.CancellationToken))]
```

Note that the attribute is removed from the metadata of the compiled assembly unless the `TRAX_ANALYZERS_ANNOTATIONS` symbol is defined. Use the assembly-level form to exclude the methods of an assembly that does not define this symbol.

## NonAwaitableTypeAttribute

Use `NonAwaitableTypeAttribute` to exclude TRX0042/TRX0045 `await` recommendations for specific types at the assembly level.
The match is exact-type only. Derived types are not excluded unless explicitly listed.

```csharp
[assembly: Trax.Analyzers.Annotations.NonAwaitableTypeAttribute(typeof(System.Data.Common.DbCommand))]
```

## NonAsyncDisposableTypeAttribute

Use `NonAsyncDisposableTypeAttribute` to exclude TRX0042/TRX0045 `await using` recommendations for specific types at the assembly level.
The match is exact-type only. Derived types are not excluded unless explicitly listed.

```csharp
[assembly: Trax.Analyzers.Annotations.NonAsyncDisposableTypeAttribute(typeof(System.Data.Common.DbCommand))]
```

## CultureInsensitiveAttribute

Use `CultureInsensitiveAttribute` to mark the value of a property, a field, or a parameter as culture-insensitive, even when its type is culture-sensitive. Methods cannot be annotated: the attribute marks a value, not the way a method computes it.

```csharp
class Sample
{
    [Trax.Analyzers.Annotations.CultureInsensitive]
    public static double InvariantValue => 0;
}

_ = $"{Sample.InvariantValue}"; // No TRX0076 diagnostic
```

A parameter marked with the attribute is culture-insensitive in both directions: reading it in the method does not report a diagnostic, and TRX0075/TRX0076 are not reported for the arguments provided by the callers.

```csharp
static void Log([Trax.Analyzers.Annotations.CultureInsensitive] string message) { }

Log($"Value: {1.5}"); // No TRX0076 diagnostic
```

Properties and fields of another assembly can be annotated at the assembly level using their XML documentation id.

```csharp
[assembly: Trax.Analyzers.Annotations.CultureInsensitiveAttribute("P:Sample.InvariantValue")]
```
