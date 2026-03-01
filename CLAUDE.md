# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Is

A sibling repository to the main `hq` orchestrator. Contains 7 command plugins and 1 logging plugin, compiled as dynamically-loaded .NET assemblies that HQ discovers at runtime via `AssemblyLoadContext`.

## Build Commands

```bash
# Build entire solution
dotnet build HQ.Plugins.sln

# Build a single plugin
dotnet build HQ.Plugins.GoogleCalendar/HQ.Plugins.GoogleCalendar.csproj

# Build release
dotnet build HQ.Plugins.sln -c Release
```

There are no test projects in this repository. Plugin tests live in the main `hq` repo's test suite.

## Architecture

### Plugin Contract

Every command plugin extends `CommandBase<ServiceRequest, ServiceConfig>` from `HQ.Models` (supplied by the host, not bundled). The contract:

- **`Name`** / **`Description`** — plugin identity
- **`GetToolDefinitions()`** — returns LLM-callable tools via annotation scanning
- **`DoWork()`** — dispatches to `this.ProcessRequest(...)` which routes by `serviceRequest.Method`

The logging plugin (`HQ.Logging.FileLogger`) uses `LoggingBase<LoggingConfig>` instead.

### Tool Definition Pattern (Annotations)

Tools are declared with three attributes on methods:

```csharp
[Display(Name = "snake_case_tool_name")]    // must match serviceRequest.Method exactly
[Description("Natural language for the LLM")]
[Parameters("""{"type":"object","properties":{...},"required":[...]}""")]
public async Task<object> MethodName(ServiceConfig config, ServiceRequest serviceRequest) { ... }
```

Two variants exist:
- **Self-annotating** — annotations on the Command class itself, `GetToolDefinitions()` returns `this.GetServiceToolCalls()`. Used by: HomeAssistantAssist, PythonRunner, Telegram, UseMemos, WebSearch.
- **Service-class annotating** — annotations on a separate `*Service` class, `GetToolDefinitions()` returns `ServiceExtensions.GetServiceToolCalls<TService>()`. Used by: GoogleCalendar (`CalService`), SupportChannelKb (`SupportChannelKbService`).

### Model Pattern

Each plugin defines two records in a `Models/` directory:
- **`ServiceConfig : IPluginConfig`** — plugin-specific config (API keys, URLs). Stored as encrypted JSON in HQ's PostgreSQL database.
- **`ServiceRequest : IPluginServiceRequest`** — carries `Method`, `ToolCallId`, `RequestingService`, `ConfirmationId`, plus plugin-specific fields.

### .csproj Requirements

All plugins must have:
```xml
<TargetFramework>net9.0</TargetFramework>
<EnableDynamicLoading>true</EnableDynamicLoading>
<AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
<AppendRuntimeIdentifierToOutputPath>false</AppendRuntimeIdentifierToOutputPath>
```

Debug output targets the host's Plugins directory:
```xml
<OutputPath>..\..\hq\HQ\bin\Debug\net9.0\Plugins\</OutputPath>
```

`HQ.Models` must be referenced with `Private=false` and `ExcludeAssets=runtime` to avoid type identity conflicts across assembly load contexts.

## Key Interfaces (from HQ.Models)

- **`IPluginConfig`** — `Name`, `Description`
- **`IPluginServiceRequest`** — `Method`, `ToolCallId`, `RequestingService`, `ConfirmationId`
- **`INotificationService`** — confirmation flows (`RequestConfirmation`, `DoesConfirmationExist`, `Confirm`)
- **`INotificationPlugin`** — only Telegram implements this (receives confirmation notifications)
- **`OrchestratorRequest`** — message between plugins and orchestrator: `{Service, ServiceRequest, ToolCallId}`

## Notable Plugin Details

- **HomeAssistantAssist**: namespace is `HQ.Plugins.HomeAssistantVoice` despite the project name — historical artifact, don't "fix" it.
- **Telegram**: most complex plugin. References `HQ.Services` directly, uses `ServiceResolver.GetOrchestrator()` and `MessageCache` for routing inbound messages back through HQ. Implements `INotificationPlugin`. Overrides `Initialize()` to start a long-poll loop.
- **GoogleCalendar**: OAuth tokens cached in `GoogleCalendar/` subdirectory next to the DLL via `FileDataStore`. Has a `CalendarMethods` constants class for tool name strings.
- **UseMemos**: `add_memo` uses a confirmation flow (two-call pattern: first returns a `ConfirmationId`, second validates and executes).
- **PythonRunner**: writes script to temp file, runs `python` via `Process.Start`, assumes `python` is on PATH.
