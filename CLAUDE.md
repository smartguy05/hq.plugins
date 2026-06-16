# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Is

A sibling repository to the main `hq` orchestrator. Contains 43 command plugins and 1 logging plugin (`HQ.Logging.FileLogger`), compiled as dynamically-loaded .NET assemblies that HQ discovers at runtime via `AssemblyLoadContext`.

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
- **Self-annotating** — annotations on the Command class itself, `GetToolDefinitions()` returns `this.GetServiceToolCalls()`. Used by: HomeAssistantAssist, Telegram, UseMemos, WebSearch, ReportGenerator, WebReader.
- **Service-class annotating** — annotations on a separate `*Service` class, `GetToolDefinitions()` returns `ServiceExtensions.GetServiceToolCalls<TService>()`. Used by most integrations: GoogleCalendar (`CalService`), SupportChannelKb, HubSpot, LinkedIn, Asana, Jira, and all the finance/office/e-commerce plugins below.

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
- **`INotificationPlugin`** — implemented by Telegram and Slack (receive confirmation notifications; Slack via Socket Mode)
- **`IHasFrontend`** / **`IDatabasePlugin`** / **`IHasHttpRoutes`** — implemented by self-hosted, stateful plugins (e.g. `Tasks`): a plugin can ship its own UI, own EF Core schema/migrations, and HTTP routes
- **`OrchestratorRequest`** — message between plugins and orchestrator: `{Service, ServiceRequest, ToolCallId}`

## Notable Plugin Details

- **HomeAssistantAssist**: namespace is `HQ.Plugins.HomeAssistantVoice` despite the project name — historical artifact, don't "fix" it.
- **Telegram**: most complex plugin. References `HQ.Services` directly, uses `ServiceResolver.GetOrchestrator()` and `MessageCache` for routing inbound messages back through HQ. Implements `INotificationPlugin`. Overrides `Initialize()` to start a long-poll loop.
- **GoogleCalendar**: OAuth tokens cached in `GoogleCalendar/` subdirectory next to the DLL via `FileDataStore`. Has a `CalendarMethods` constants class for tool name strings.
- **UseMemos**: `add_memo` uses a confirmation flow (two-call pattern: first returns a `ConfirmationId`, second validates and executes).
- **HubSpot**: CRM integration (10 tools). Uses HubSpot CRM API v3 with Private App token auth. Manages contacts, deals, companies, and notes. Has `HubSpotClient` HTTP wrapper.
- **LinkedIn**: LinkedIn profile management and Proxycurl enrichment (8 tools). Two API sources: LinkedIn Community Management API (OAuth) for posting, Proxycurl for people/company search. Proxycurl calls cost ~$0.01 each.
- **ReportGenerator**: Self-annotating (3 tools). Generates reports from Markdown content using Markdig. Outputs PDF, HTML, or Markdown files. Persists a report index JSON for retrieval.
- **WebReader**: Self-annotating (3 tools: `read_page`, `extract_links`, `search_page`). Token-efficient page reading — renders with Playwright/Chromium (shared lazily-launched browser, fresh context per request via `PlaywrightRenderer`), then runs the Jina/Firecrawl-style pipeline: SmartReader (Mozilla Readability port) extracts main content, ReverseMarkdown converts it to markdown. Pure conversion logic lives in `ReaderPipeline` (browser-free, unit-tested). Complements `HeadlessBrowser` (interaction/testing) — this one is read-only. Installs Chromium on `Initialize()` like HeadlessBrowser. Typical output is ~10x smaller than raw HTML.
- **GoogleWorkspace**: Service-class annotating (`GoogleWorkspaceService`, 20 tools) covering Drive (`drive_*`: list/search/get/download/upload/create_folder/move/copy/delete/share), Docs (`docs_create`/`docs_get_text`/`docs_append_text`/`docs_replace_text`), and Sheets (`sheets_create`/`sheets_get_values`/`sheets_update_values`/`sheets_append_row`/`sheets_clear_values`/`sheets_list_sheets`). Reuses the GoogleCalendar OAuth pattern: a copied `GoogleApiCredentials` record with `[OAuthProvider]` scopes widened to drive+documents+spreadsheets, refresh-token `UserCredential` built per request via `GoogleClientFactory`. Per-surface logic in `Clients/{Drive,Docs,Sheets}Client.cs`; pure helpers `DriveClient.DefaultExportMime`, `SheetsClient.NormalizeValues`, `DocsClient.ExtractText` are unit-tested. `FileId` doubles as Drive fileId / Docs documentId / Sheets spreadsheetId.
- **Microsoft365**: Service-class annotating (`Microsoft365Service`, 17 tools) covering OneDrive/SharePoint files (`files_*`), Excel (`excel_*`), and Word (`word_create`/`word_read`). Reuses the Teams Graph pattern: app-only `ClientSecretCredential` + `GraphServiceClient` via `GraphClientFactory` (config needs `TenantId`/`ClientId`/`ClientSecret`/`DefaultDriveId`; Azure app needs `Files.ReadWrite.All` + `Sites.ReadWrite.All`). Files/Word use the typed Graph SDK (`Graph/FilesClient.cs`, `Graph/WordClient.cs`); Excel uses raw Graph REST (`Graph/ExcelClient.cs`) because cell-value payloads map to JSON better than the SDK's `UntypedNode`. Word create/read goes through OpenXML (`DocxHelper`) since Graph has no live Word-edit API. Pure helpers `A1Helper` and `DocxHelper` are unit-tested.
- **Stripe**: Service-class annotating (`StripeService`, 10 tools) for payments/invoicing via the official `Stripe.net` SDK (no custom client — `RequestOptions { ApiKey }` per call, so no global state). Money-moving tools (`create_invoice`/`send_invoice`/`create_payment_link`/`create_refund`) are `[SupportsConfirmation]` and gated on `config.RequiresConfirmation` via the service-class confirmation pattern (see below). Config is just `ApiKey` + `RequiresConfirmation`.
- **Zendesk**: Service-class annotating (`ZendeskService`, 10 tools) for support tickets/users/macros. Raw `ZendeskClient` copied from `AsanaClient` but with **Basic auth** (`base64("{email}/token:{apiToken}")`) and a single 429 retry honoring `Retry-After`. `add_ticket_comment` has an explicit `public` flag (customer-facing vs internal note). `apply_macro` renders the macro then PUTs the resulting ticket. Pairs with SupportChannelKb.
- **Calendly**: Service-class annotating (`CalendlyService`, 7 tools) for external scheduling. Bearer-token `CalendlyClient` (AsanaClient template). Booking happens on Calendly's hosted page — tools surface event types, generate single-use links, and read/cancel events. List calls resolve the current user via `/users/me` when no URI is passed. Pure helper `CalendlyService.Uuid` (URI→trailing segment) is unit-tested.
- **QuickBooks**: Service-class annotating (`QuickBooksService`, 11 tools) for bookkeeping (invoices, customers, expenses, bills, accounts, vendors, reports). `QuickBooksClient` exchanges the stored refresh token for an access token at the Intuit token endpoint on first use, then calls the Realm-scoped v3 API (`minorversion=70`); `UseSandbox` toggles the base URL. Auth reuses the GoogleWorkspace `[OAuthProvider]` shape (`IntuitCredentials`) + a `RealmId` config field. All writes are `[SupportsConfirmation]`. v1 scope is deliberately tight — payroll, journal entries, and transaction recategorization are deferred (no `categorize_transaction`).
- **DocuSign**: Service-class annotating (`DocuSignService`, 7 tools) for e-signature via the official `DocuSign.eSign.dll` SDK. Auth is OAuth **JWT grant** (`ApiClient.RequestJWTUserToken` from an RSA private key) — requires one-time admin consent; config carries `IntegrationKey`/`UserId`/`AccountId`/`PrivateKey`/`BasePath` (demo vs prod, which also selects the `account-d`/`account` oauth host). `send_envelope`/`send_envelope_from_template`/`void_envelope` are `[SupportsConfirmation]`.

- **Mailchimp**: Service-class annotating (`MailchimpService`, 10 tools) for email marketing (audiences, members, campaigns). `MailchimpClient` (AsanaClient template, Bearer) derives the datacenter from the key suffix; pure helpers `DataCenterFromKey` + `SubscriberHash` (MD5 of lowercased email) are unit-tested. `send_campaign` is `[SupportsConfirmation]`.
- **GoogleAnalytics**: Service-class annotating (`GoogleAnalyticsService`, 3 read-only tools: `run_report`/`run_realtime_report`/`get_metadata`). Calls the GA4 Data API over **REST** (the .NET GA SDK is beta-only) using a refresh-token Google credential (`GaClient` reuses the GoogleWorkspace `[OAuthProvider]` shape with the `analytics.readonly` scope; gets a token via `GetAccessTokenForRequestAsync`). Pure helper `GaClient.NameList` (CSV→`[{name}]`) is unit-tested.
- **GoogleForms**: Service-class annotating (`GoogleFormsService`, 5 tools: create/get form, add_questions, list/get responses) via the `Google.Apis.Forms.v1` SDK + `UserCredential` (its own `forms.body`/`forms.responses.readonly` scopes — a separate consent from GoogleWorkspace, hence its own plugin). Pure helper `BuildItem` (QuestionSpec→Forms `Item`, mapping TEXT/PARAGRAPH/RADIO/CHECKBOX/DROPDOWN) is unit-tested.
- **Shopify**: Service-class annotating (`ShopifyService`, 10 tools) for store management (products, orders, customers, inventory). `ShopifyClient` (AsanaClient template) authenticates with the `X-Shopify-Access-Token` header; REST Admin API (custom-app token), `ApiVersion` configurable. `fulfill_order` uses the 2025 fulfillment-orders flow. Writes (`create_product`/`update_inventory`/`fulfill_order`/`create_draft_order`) are `[SupportsConfirmation]`.
- **Ramp**: Service-class annotating (`RampService`, 8 read-only tools: transactions/cards/reimbursements/users/departments/limits). `RampClient` mints a token via the OAuth **client-credentials** grant (Basic `clientId:clientSecret`) on first use, like QuickBooksClient; `UseSandbox` toggles `demo-api.ramp.com`. List responses unwrap the `data` array.
- **Gusto**: Service-class annotating (`GustoService`, 8 **read-only** tools: company/employees/payrolls/time-off/locations/pay-schedules). `GustoClient` refresh-token fetch (QuickBooksClient style); `UseDemo` toggles `api.gusto-demo.com`. Company id resolves from `/v1/me` (`roles.payroll_admin.companies[0].uuid`) when omitted. v1 is read-only — Gusto's write surface (time-off) runs through embedded flows, deferred.
- **Square**: Service-class annotating (`SquareService`, 12 tools) spanning locations, catalog/inventory, customers, payments/orders, and bookings/appointments — one plugin for the local/physical/service SMB segment. `SquareClient` (AsanaClient template, Bearer + `Square-Version` header), **raw REST not the SDK** (the SDK churns across major versions); `UseSandbox` toggles `connect.squareupsandbox.com`. Most calls are location-scoped (`DefaultLocationId` fallback). `create_booking`/`cancel_booking` notify customers → `[SupportsConfirmation]`. v1 defers catalog/payment writes (overlaps Stripe/QuickBooks).

#### Communications
- **Slack**: Send/receive Slack messages (6 tools: `send_slack_message`/`upload_slack_file`/`download_slack_file`/`open_slack_dm`/`list_slack_users`/`list_slack_channels`). Implements `INotificationPlugin` and receives confirmation notifications via **Socket Mode** (like Telegram).
- **Teams**: Send/receive Microsoft Teams messages (5 tools: `send_teams_message`/`list_teams`/`list_teams_channels`/`send_teams_file`/`download_teams_file`). Uses the Microsoft Graph app-only auth pattern (shared with Microsoft365).
- **Twilio**: SMS/WhatsApp/voice/verification/conversations (15 tools: `send_sms`/`send_whatsapp`/`make_call`/`list_recordings`/`lookup_phone_number`/`send_verification`/`check_verification`/`create_conversation`/… ). Pairs with the planned Voice (TTS/STT) plugin for phone-based interaction.
- **Perplexity**: Web research via Perplexity Sonar models, returning cited answers (2 tools: `perplexity_search`, `perplexity_deep_research`). Complements `WebSearch` (link results) and `WebReader` (page content).

#### Project management
- **Asana**: Task/project management (16 tools: `create_task`/`update_task`/`get_tasks`/`search_tasks`/`move_task_to_section`/`set_parent_for_task`/… ). `AsanaClient` (Bearer) is the **template most later REST clients are copied from** (Zendesk, Calendly, Mailchimp, Shopify, Square, …).
- **Jira**: Jira Cloud project management (21 tools, all `jira_*`: create/update/transition/assign issues, comments, worklogs, sprints, boards, issue links, user/issue search).
- **Tasks**: Self-hosted task manager — projects/tasks/comments (11 tools). EF Core-backed; implements `IHasFrontend`, `IDatabasePlugin`, `IHasHttpRoutes` (ships its own UI, schema, and routes). A local replacement for Asana when no external CRM is wanted.

#### Development & infrastructure
- **ClaudeCode**: Software-engineering agent powered by Claude Code running in Docker containers (7 tools: `claude_code_task`/`claude_code_continue`/`claude_code_review`/`claude_code_status`/`claude_code_get_diff`/`claude_code_create_pr`/`claude_code_destroy_session`).
- **HeadlessBrowser**: Playwright/Chromium browser automation for navigation, content extraction, form-filling, clicking, and screenshots (13 tools). Lazy-launched shared browser, fresh context per request; installs Chromium on `Initialize()`. The interaction/testing counterpart to read-only `WebReader`.
- **FileStorage**: Docker-based sandboxed file workspaces with Python and Node.js (11 tools, all `workspace_*`: create/destroy/list, read/write/delete files, `exec`/`exec_script`, copy-between). Sandboxed execution for agent-generated code/files.

#### Media
- **ImageGeneration**: Image generation/editing/description via Google Gemini image models (3 tools: `generate_image`, `describe_image`, `edit_image`).

#### Personal assistant (daily-life layer)
- **Weather**: Service-class annotating (`WeatherService`, 3 read-only tools: `get_current_weather`/`get_forecast`/`get_weather_alerts`). OpenWeatherMap One Call 3.0 + Geocoding; `WeatherClient` (API-key query param, CalendlyClient template). Locations accept a place name (geocoded) or explicit lat/lon. Pure helper `WeatherService.NormalizeUnits` (metric/imperial/standard) is unit-tested.
- **Maps**: Service-class annotating (`MapsService`, 5 read-only tools: `get_directions`/`get_travel_time`/`search_places`/`get_place_details`/`geocode_address`). Google Maps Platform JSON web-services (Directions, Distance Matrix, Places Text Search, Place Details, Geocoding); `MapsClient` appends the API key per call. Maps returns HTTP 200 with a logical `status`, so `MapsService.Result` surfaces non-OK statuses as failures. Pure helper `NormalizeMode` is unit-tested.
- **Notion**: Service-class annotating (`NotionService`, 6 tools: `notion_search`/`notion_get_page`/`notion_create_page`/`notion_append_block`/`notion_query_database`/`notion_update_page`). `NotionClient` (Bearer + `Notion-Version` header, AsanaClient template). Simple cases use title/text; advanced callers pass raw Notion JSON (`propertiesJson`/`childrenJson`/`filterJson`/`sortsJson`) since property schemas vary per database. Pure helpers `RichText` + `ParagraphBlocks` (text→blocks) are unit-tested.
- **GoogleContacts**: Service-class annotating (`GoogleContactsService`, 5 tools: `list_contacts`/`search_contacts`/`get_contact`/`create_contact`/`update_contact`). Google People API typed SDK (`Google.Apis.PeopleService.v1`) + the GoogleForms refresh-token `UserCredential` pattern (its own `contacts` scope). `update_contact` fetches the etag first and only sends touched fields. Pure helper `BuildPerson` is unit-tested.
- **Plaid**: Service-class annotating (`PlaidService`, 3 read-only tools: `list_accounts`/`get_balances`/`list_transactions`). `PlaidClient` injects `client_id`/`secret` into every JSON body (Plaid's auth model); `Environment` toggles sandbox/production. The per-item `access_token` comes from the Plaid Link setup flow (host-side) and is **long-lived — no refresh**. Pure helpers `BaseUrlFor` + `Date` are unit-tested.
- **Health**: Service-class annotating (`HealthService`, 5 read-only tools: `list_health_users`/`get_sleep`/`get_activity`/`get_daily`/`get_body`). Terra wearables aggregator (one integration, many devices); `TerraClient` uses `dev-id` + `x-api-key` headers. Data tools default to the last 7 days. Pure helpers `DataPath` + `Date` are unit-tested.
- **DocumentAI**: Service-class annotating (`DocumentAiService`, 3 tools: `extract_text`/`extract_receipt`/`extract_document_fields`). Plain OCR uses Cloud Vision (`images:annotate`, DOCUMENT_TEXT_DETECTION); receipts/forms use Document AI processors (config carries `ProjectId`/`Location`/`ReceiptProcessorId`/`DocumentProcessorId`). Auth reuses the Google refresh-token credential (cloud-platform scope) via `Google.Apis.Auth`, fetching a bearer token per request with `GetAccessTokenForRequestAsync`; raw REST through `DocumentAiClient`. Pure helper `ProcessorUrl` is unit-tested.

### Confirmation flow (service-class plugins)
Stripe/QuickBooks/DocuSign/Mailchimp/Shopify/Square follow the `EmailService` confirmation pattern: the `*Service` takes `INotificationService` in its constructor (passed from the `*Command`), a `bool RequiresConfirmation` lives on `ServiceConfig`, and guarded methods are marked `[SupportsConfirmation]`. A private `Confirm(...)` helper returns `RequestConfirmation(...)` on the first call (no `ConfirmationId`) and checks `DoesConfirmationExist(...)` on the second before executing.
