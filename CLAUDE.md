# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Is

A sibling repository to the main `hq` orchestrator. Contains 11 command plugins and 1 logging plugin, compiled as dynamically-loaded .NET assemblies that HQ discovers at runtime via `AssemblyLoadContext`.

## Build Commands

```bash
# Build entire solution
dotnet build HQ.Plugins.sln

# Build a single plugin
dotnet build HQ.Plugins.GoogleCalendar/HQ.Plugins.GoogleCalendar.csproj

# Build release
dotnet build HQ.Plugins.sln -c Release
```

## Test Commands

```bash
# Run all plugin tests
dotnet test HQ.Plugins.Tests/HQ.Plugins.Tests.csproj

# Run tests for a specific plugin
dotnet test HQ.Plugins.Tests --filter "FullyQualifiedName~Email"

# Exclude integration tests (require live services)
dotnet test HQ.Plugins.Tests --filter "Category!=Integration"
```

Plugin tests live alongside the plugins in this repo under `HQ.Plugins.Tests/`.

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
- **Self-annotating** — annotations on the Command class itself, `GetToolDefinitions()` returns `this.GetServiceToolCalls()`. Used by: HomeAssistantAssist, PythonRunner, Telegram, UseMemos, WebSearch, ReportGenerator.
- **Service-class annotating** — annotations on a separate `*Service` class, `GetToolDefinitions()` returns `ServiceExtensions.GetServiceToolCalls<TService>()`. Used by: GoogleCalendar (`CalService`), SupportChannelKb (`SupportChannelKbService`), HubSpot (`HubSpotService`), LinkedIn (`LinkedInService`), JobBoard (`JobBoardService`).

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

`HQ.Models` is consumed as a NuGet package with `ExcludeAssets="runtime"` to avoid type identity conflicts across assembly load contexts (the host provides the assembly at runtime):
```xml
<PackageReference Include="HQ.Models" Version="1.0.0" ExcludeAssets="runtime" PrivateAssets="none" />
```

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
- **HubSpot**: CRM integration (10 tools). Uses HubSpot CRM API v3 with Private App token auth. Manages contacts, deals, companies, and notes. Has `HubSpotClient` HTTP wrapper.
- **LinkedIn**: LinkedIn profile management and Proxycurl enrichment (8 tools). Two API sources: LinkedIn Community Management API (OAuth) for posting, Proxycurl for people/company search. Proxycurl calls cost ~$0.01 each.
- **ReportGenerator**: Self-annotating (3 tools). Generates reports from Markdown content using Markdig. Outputs HTML or Markdown files. Persists a report index JSON for retrieval.
- **JobBoard**: Multi-source job aggregator (6 tools). Clients for Indeed (RapidAPI), Upwork (RSS), LinkedIn Jobs (Proxycurl), Toptal (scraping). Includes application tracking persisted to local JSON.
