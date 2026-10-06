---
layout: default
title: ILogLevelService
description: "Reference for ILogLevelService, which reads and sets the log level of each category configured under Logging:LogLevel at runtime, in this process."
parent: Scheduler API
grand_parent: SDK Reference
nav_order: 16
---

# ILogLevelService

Reads and sets, at runtime, the log level of each category the host configures under `Logging:LogLevel`. The dashboard's server settings and the GraphQL API both call it, so the two read the same levels and change them the same way. Registered as a singleton by `AddScheduler(...)`.

A level set here is applied to the host's `LoggerFilterOptions` as a post-configure step, and the options are signalled to change, so the logger factory re-reads its filters at once. The level sits over every configuration source and survives a reload of the host's configuration. Nothing is persisted: it lasts until the process restarts, and it changes the logging of the process that served the call only, not of the other processes of the deployment.

## Signature

```csharp
namespace Trax.Scheduler.Services.LogLevels;

public interface ILogLevelService
{
    IReadOnlyList<CategoryLogLevel> GetLogLevels();
    LogLevelUpdateResult SetLogLevels(IReadOnlyList<LogLevelChange> levels);
}

public record CategoryLogLevel(string Category, LogLevel Level, string? ConfiguredLevel, bool Overridden);
public record LogLevelChange(string Category, LogLevel Level);
public record LogLevelUpdateResult(bool Success, int Count, IReadOnlyList<string> NotApplied, string Message);
```

## GetLogLevels

One `CategoryLogLevel` per category under `Logging:LogLevel` (and per category set here since), `Default` first and the rest by name.

| Field | Meaning |
|---|---|
| `Category` | The category; `Default` is the level every other category falls back to |
| `Level` | The level the loggers filter the category at now: a level set here when there is one, otherwise the configured one, or `Information` when the configured value is not a level |
| `ConfiguredLevel` | The configured value as written; null for a category not configured |
| `Overridden` | Whether a level was set here, so `Level` is not the configured one |

## SetLogLevels

Sets each category's level, then reads back the level each is filtered at. Category names are matched ignoring case, and a category given twice takes the last level.

Only a category configured under `Logging:LogLevel`, or one set here before, can be set, so a call cannot turn on logging the host never configured. A list naming any other category, an undefined `LogLevel` value, or an empty list is refused whole: `Success` is false and nothing is set.

On success `Count` is the number of categories set. `NotApplied` lists any whose loggers still filter at another level, which happens when the host adds its own filter rule for the category after Trax; the message names them too.

## Package

```
dotnet add package Trax.Scheduler
```
