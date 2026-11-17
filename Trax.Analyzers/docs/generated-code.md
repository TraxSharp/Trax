# Analyzing generated code

The rules skip generated code, which is the expected behavior for the vast majority of projects: you cannot fix code
you do not own, and analyzing code nobody looks at costs build time. Some rules are the exception and analyze it
anyway: the ones whose subject is the generated file itself, such as the Blazor rules that work on the code generated
from the `.razor` files, and the ones that need to see the whole compilation to be correct. Set the
`TRAX_ANALYZERS_GENERATED_CODE` environment variable to opt in to analyzing generated code with every rule.

## Configuration

The `report_generated_code` option indicates whether a rule reports the diagnostics located in the generated code it
analyzes. It can be set for a single rule, or for all of them at once with the `MA` prefix:

```ini
[*.cs]
# All the rules report the diagnostics located in generated code
MA.report_generated_code = true

# TRX0051 does not, as a rule specific value wins over the value of all the rules
TRX0051.report_generated_code = false
```

| Key | Applies to |
|-----|------------|
| `MA.report_generated_code` | All the rules |
| `TRX0001.report_generated_code` | The rule `TRX0001`, and wins over `MA.report_generated_code` |

Both keys are read from the `.editorconfig` section of the generated file, so they can be set for a specific set of
generated files:

```ini
[Generated/**.cs]
TRX0051.report_generated_code = true
```

When neither is set, each rule uses its own default, which is to not report in generated code, except for the rules
below. To turn off a rule entirely, set its severity to `none` instead.

This option only decides what a rule reports, not what it analyzes, so `report_generated_code = true` has no effect on
a rule that does not analyze generated code: it needs the environment variable below. Setting it to `false` always
works, as a rule can only report what it is allowed to report.

## Opting in with the environment variable

Set the `TRAX_ANALYZERS_GENERATED_CODE` environment variable to analyze generated code with every rule:

| Value | Behavior |
|-------|----------|
| `true` (case-insensitive) or `1` | The rules analyze generated code, and `report_generated_code` decides what they report |
| not set, empty, or any other value | The rules skip generated code, except the ones that need it |

Two kinds of rules analyze generated code without the variable: the ones that report in generated code by default,
listed below, and the ones that need to see the whole compilation to be correct, such as TRX0053, which reports a class
that no other class inherits from and would report a false positive if the deriving class were declared in a generated
file. `report_generated_code` works on those rules whether the variable is set or not.

## Rules reporting in generated code by default

<!-- rules -->

|Id|Description|
|--|-----------|
|[TRX0115](https://github.com/TraxSharp/Trax/blob/main/Trax.Analyzers/docs/rules/TRX0115.md)|Unknown component parameter|
|[TRX0116](https://github.com/TraxSharp/Trax/blob/main/Trax.Analyzers/docs/rules/TRX0116.md)|Parameters with \[SupplyParameterFromQuery\] attributes should also be marked as \[Parameter\]|
|[TRX0117](https://github.com/TraxSharp/Trax/blob/main/Trax.Analyzers/docs/rules/TRX0117.md)|Parameters with \[EditorRequired\] attributes should also be marked as \[Parameter\]|
|[TRX0118](https://github.com/TraxSharp/Trax/blob/main/Trax.Analyzers/docs/rules/TRX0118.md)|\[JSInvokable\] methods must be public|
|[TRX0119](https://github.com/TraxSharp/Trax/blob/main/Trax.Analyzers/docs/rules/TRX0119.md)|JSRuntime must not be used in OnInitialized or OnInitializedAsync|
|[TRX0120](https://github.com/TraxSharp/Trax/blob/main/Trax.Analyzers/docs/rules/TRX0120.md)|Use InvokeVoidAsync when the returned value is not used|
|[TRX0121](https://github.com/TraxSharp/Trax/blob/main/Trax.Analyzers/docs/rules/TRX0121.md)|Do not overwrite parameter value|
|[TRX0122](https://github.com/TraxSharp/Trax/blob/main/Trax.Analyzers/docs/rules/TRX0122.md)|Parameters with \[SupplyParameterFromQuery\] attributes are only valid in routable components (@page)|
|[TRX0123](https://github.com/TraxSharp/Trax/blob/main/Trax.Analyzers/docs/rules/TRX0123.md)|Sequence number must be a constant|
|[TRX0124](https://github.com/TraxSharp/Trax/blob/main/Trax.Analyzers/docs/rules/TRX0124.md)|Microsoft.Extensions.Logging parameter type is not valid|
|[TRX0125](https://github.com/TraxSharp/Trax/blob/main/Trax.Analyzers/docs/rules/TRX0125.md)|The list of log parameter types contains an invalid type|
|[TRX0126](https://github.com/TraxSharp/Trax/blob/main/Trax.Analyzers/docs/rules/TRX0126.md)|The list of log parameter types contains a duplicate|
|[TRX0135](https://github.com/TraxSharp/Trax/blob/main/Trax.Analyzers/docs/rules/TRX0135.md)|The log parameter has no configured type|
|[TRX0139](https://github.com/TraxSharp/Trax/blob/main/Trax.Analyzers/docs/rules/TRX0139.md)|Serilog parameter type is not valid|
|[TRX0144](https://github.com/TraxSharp/Trax/blob/main/Trax.Analyzers/docs/rules/TRX0144.md)|Use System.OperatingSystem to check the current OS|
|[TRX0153](https://github.com/TraxSharp/Trax/blob/main/Trax.Analyzers/docs/rules/TRX0153.md)|Do not log symbols decorated with DataClassificationAttribute directly|
|[TRX0190](https://github.com/TraxSharp/Trax/blob/main/Trax.Analyzers/docs/rules/TRX0190.md)|Use partial property instead of partial method for GeneratedRegex|
|[TRX0195](https://github.com/TraxSharp/Trax/blob/main/Trax.Analyzers/docs/rules/TRX0195.md)|Do not use static fields before they are initialized|

<!-- rules -->

## What is generated code

A file is considered generated when:

- the `.editorconfig` file sets `generated_code` for the file, which takes precedence over the rest,
- or its name is `*.designer.cs`, `*.generated.cs`, `*.g.cs`, `*.g.i.cs`, or starts with `TemporaryGeneratedFile_`,
- or it starts with an `<auto-generated>` comment.

Only the file of the diagnostic is considered. A `[GeneratedCode]` or `[DebuggerNonUserCode]` attribute in a hand
written file does not make the code generated, and a partial type declared in a generated file and in a hand written
one reports only in the hand written file. Use the `generated_code` option below for the files the detection does not
recognize.

The rules that do not analyze generated code never see it, so they follow the detection of Roslyn instead, which also
considers the symbols marked with `[GeneratedCode]` or `[DebuggerNonUserCode]` generated. The two detections only
differ for a rule the variable opted in.

## Using the `generated_code` option

Roslyn supports the `generated_code` option, which overrides the detection above for a set of files. Unlike
`report_generated_code` and the environment variable, it applies to **all** the analyzers, not only to
Trax.Analyzers:

```ini
[Generated/**.cs]
generated_code = false
```

Use `report_generated_code` when you want Trax.Analyzers specifically to report everything Roslyn considers
generated, and `generated_code` when you want all the analyzers to treat a specific set of files as regular code.

## Why an environment variable

An analyzer must declare how it handles generated code from `Initialize(AnalysisContext)`, and the options of the
`.editorconfig` files are not available at that point: they can only be read from the analysis callbacks, which run
later. An environment variable is the only configuration that can be read early enough, which is why the global
opt-in is not an `.editorconfig` option.

This has consequences that are worth knowing:

- **The compiler server caches the value.** `dotnet build` and `msbuild` run the analyzers inside `VBCSCompiler`,
  which is reused across builds and keeps the environment it was started with. Run `dotnet build-server shutdown`
  after changing the variable.
- **The IDEs cache it too.** Visual Studio, Rider, and the C# extension of Visual Studio Code run the analyzers in
  their own long-lived processes. Restart the IDE after changing the variable.
- **Changing the variable does not invalidate the build.** It is not a compiler input, so MSBuild considers the
  projects up-to-date and skips the compilation. Rebuild the solution to see the new diagnostics.
- **There is no MSBuild property.** MSBuild can only pass environment variables to a process it starts itself,
  which is not the case when the compilation is delegated to the compiler server. A property would work on some
  machines and silently do nothing on others.
