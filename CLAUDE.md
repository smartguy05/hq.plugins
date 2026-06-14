# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Is

A sibling repository to the main `hq` orchestrator. Contains command plugins and 1 logging plugin, compiled as dynamically-loaded .NET assemblies that HQ discovers at runtime via `AssemblyLoadContext`.

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
- **WebReader**: Self-annotating (3 tools: `read_page`, `extract_links`, `search_page`). Token-efficient page reading — renders with Playwright/Chromium (shared lazily-launched browser, fresh context per request via `PlaywrightRenderer`), then runs the Jina/Firecrawl-style pipeline: SmartReader (Mozilla Readability port) extracts main content, ReverseMarkdown converts it to markdown. Pure conversion logic lives in `ReaderPipeline` (browser-free, unit-tested). Complements `HeadlessBrowser` (interaction/testing) — this one is read-only. Installs Chromium on `Initialize()` like HeadlessBrowser. Typical output is ~10x smaller than raw HTML.
- **GoogleWorkspace**: Service-class annotating (`GoogleWorkspaceService`, 20 tools) covering Drive (`drive_*`: list/search/get/download/upload/create_folder/move/copy/delete/share), Docs (`docs_create`/`docs_get_text`/`docs_append_text`/`docs_replace_text`), and Sheets (`sheets_create`/`sheets_get_values`/`sheets_update_values`/`sheets_append_row`/`sheets_clear_values`/`sheets_list_sheets`). Reuses the GoogleCalendar OAuth pattern: a copied `GoogleApiCredentials` record with `[OAuthProvider]` scopes widened to drive+documents+spreadsheets, refresh-token `UserCredential` built per request via `GoogleClientFactory`. Per-surface logic in `Clients/{Drive,Docs,Sheets}Client.cs`; pure helpers `DriveClient.DefaultExportMime`, `SheetsClient.NormalizeValues`, `DocsClient.ExtractText` are unit-tested. `FileId` doubles as Drive fileId / Docs documentId / Sheets spreadsheetId.
- **Microsoft365**: Service-class annotating (`Microsoft365Service`, 17 tools) covering OneDrive/SharePoint files (`files_*`), Excel (`excel_*`), and Word (`word_create`/`word_read`). Reuses the Teams Graph pattern: app-only `ClientSecretCredential` + `GraphServiceClient` via `GraphClientFactory` (config needs `TenantId`/`ClientId`/`ClientSecret`/`DefaultDriveId`; Azure app needs `Files.ReadWrite.All` + `Sites.ReadWrite.All`). Files/Word use the typed Graph SDK (`Graph/FilesClient.cs`, `Graph/WordClient.cs`); Excel uses raw Graph REST (`Graph/ExcelClient.cs`) because cell-value payloads map to JSON better than the SDK's `UntypedNode`. Word create/read goes through OpenXML (`DocxHelper`) since Graph has no live Word-edit API. Pure helpers `A1Helper` and `DocxHelper` are unit-tested.
- **Stripe**: Service-class annotating (`StripeService`, 10 tools) for payments/invoicing via the official `Stripe.net` SDK (no custom client — `RequestOptions { ApiKey }` per call, so no global state). Money-moving tools (`create_invoice`/`send_invoice`/`create_payment_link`/`create_refund`) are `[SupportsConfirmation]` and gated on `config.RequiresConfirmation` via the service-class confirmation pattern (see below). Config is just `ApiKey` + `RequiresConfirmation`.
- **Zendesk**: Service-class annotating (`ZendeskService`, 10 tools) for support tickets/users/macros. Raw `ZendeskClient` copied from `AsanaClient` but with **Basic auth** (`base64("{email}/token:{apiToken}")`) and a single 429 retry honoring `Retry-After`. `add_ticket_comment` has an explicit `public` flag (customer-facing vs internal note). `apply_macro` renders the macro then PUTs the resulting ticket. Pairs with SupportChannelKb.
- **Calendly**: Service-class annotating (`CalendlyService`, 7 tools) for external scheduling. Bearer-token `CalendlyClient` (AsanaClient template). Booking happens on Calendly's hosted page — tools surface event types, generate single-use links, and read/cancel events. List calls resolve the current user via `/users/me` when no URI is passed. Pure helper `CalendlyService.Uuid` (URI→trailing segment) is unit-tested.
- **QuickBooks**: Service-class annotating (`QuickBooksService`, 11 tools) for bookkeeping (invoices, customers, expenses, bills, accounts, vendors, reports). `QuickBooksClient` exchanges the stored refresh token for an access token at the Intuit token endpoint on first use, then calls the Realm-scoped v3 API (`minorversion=70`); `UseSandbox` toggles the base URL. Auth reuses the GoogleWorkspace `[OAuthProvider]` shape (`IntuitCredentials`) + a `RealmId` config field. All writes are `[SupportsConfirmation]`. v1 scope is deliberately tight — payroll, journal entries, and transaction recategorization are deferred (no `categorize_transaction`).
- **DocuSign**: Service-class annotating (`DocuSignService`, 7 tools) for e-signature via the official `DocuSign.eSign.dll` SDK. Auth is OAuth **JWT grant** (`ApiClient.RequestJWTUserToken` from an RSA private key) — requires one-time admin consent; config carries `IntegrationKey`/`UserId`/`AccountId`/`PrivateKey`/`BasePath` (demo vs prod, which also selects the `account-d`/`account` oauth host). `send_envelope`/`send_envelope_from_template`/`void_envelope` are `[SupportsConfirmation]`.

### Confirmation flow (service-class plugins)
Stripe/QuickBooks/DocuSign follow the `EmailService` confirmation pattern: the `*Service` takes `INotificationService` in its constructor (passed from the `*Command`), a `bool RequiresConfirmation` lives on `ServiceConfig`, and guarded methods are marked `[SupportsConfirmation]`. A private `Confirm(...)` helper returns `RequestConfirmation(...)` on the first call (no `ConfirmationId`) and checks `DoesConfirmationExist(...)` on the second before executing.
