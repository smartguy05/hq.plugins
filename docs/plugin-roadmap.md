# Plugin Roadmap — AI Employee Tooling

## Purpose

HQ lets small/medium businesses "hire" AI employees they otherwise couldn't afford. The
value of an AI employee is bounded by the tools it can use — so the roadmap is organized by
**which expensive-to-hire SMB role each plugin unlocks**, not by API.

This doc covers the next five prioritized plugins. For each: the persona it unlocks, the auth
model (and which existing plugin pattern to copy), proposed tools, NuGet/API notes, effort,
and risks. All follow the established contract (see `CLAUDE.md`): `CommandBase`,
service-class annotating with `[Display]/[Description]/[Parameters]`, `ServiceConfig`/
`ServiceRequest` records, dispatch via `ProcessRequest`.

## Current coverage (post Google Workspace + Microsoft 365)

Communication (Email, Slack, Teams, Telegram, Twilio) · PM/Tasks (Asana, Jira, Tasks) ·
CRM/Sales (HubSpot, LinkedIn) · Calendar (Google) · Docs & Storage (Google Workspace,
Microsoft 365) · Research (WebSearch, Perplexity, WebReader, HeadlessBrowser) · Content
(ReportGenerator, ImageGeneration) · Dev (ClaudeCode, FileStorage) · Knowledge (UseMemos,
SupportChannelKb).

## Persona gap map

| Persona (SMB hire) | Has today | This roadmap adds |
|---|---|---|
| Bookkeeper / accountant | — | **QuickBooks**, **Stripe** |
| Customer support rep | SupportChannelKb (KB only) | **Zendesk** (ticket queue) |
| Sales / SDR | HubSpot, LinkedIn | **DocuSign**, **Calendly** |
| Exec assistant | Email, Calendar, Docs | **Calendly** |

---

## Recommended build order

Sequenced by value-to-effort. Quick high-value wins first; the auth-heavy ones last.

1. **Stripe** — S effort, high value, trivial auth. Quick win.
2. **Zendesk** — M effort, high value, pairs with existing SupportChannelKb.
3. **Calendly** — S effort, rounds out EA/SDR personas.
4. **QuickBooks** — M/L effort, highest-value finance persona; OAuth + many entities.
5. **DocuSign** — M/L effort, JWT auth is the main cost; do last.

---

## 1. Stripe — payments & invoicing  ·  Effort: S

**Persona:** bookkeeper, finance assistant. **Pairs with:** QuickBooks.

- **Auth:** secret API key (Bearer). Single `ApiKey` config field — simplest of all. Use
  **test-mode** keys for development. No OAuth.
- **SDK:** official `Stripe.net` NuGet (excellent, well-maintained) — use it rather than raw
  HTTP.
- **Config pattern to copy:** Asana-style single-token `ServiceConfig`.
- **Proposed tools (≈9):** `create_invoice`, `send_invoice`, `list_invoices`,
  `create_payment_link`, `get_customer`, `search_customers`, `create_customer`,
  `list_payments`, `create_refund`, `get_balance`.
- **Risks:** money-moving operations (refunds, payment links) should route through HQ's
  confirmation flow (`INotificationService`, two-call pattern like UseMemos `add_memo`).
  Keep test/live key separation explicit in config tooltips.

## 2. Zendesk — customer support  ·  Effort: M

**Persona:** support rep. **Pairs with:** SupportChannelKb (read ticket → search KB → draft
reply).

- **Auth:** API token via Basic auth (`{email}/token : {api_token}`) + `subdomain`. Simple;
  no OAuth needed. Config fields: `Subdomain`, `Email`, `ApiToken`.
- **API:** Zendesk Support REST API v2 (raw `HttpClient`, JSON — mirror `AsanaClient`).
- **Config pattern to copy:** Asana (base URL + token + `HttpClient` wrapper).
- **Proposed tools (≈10):** `search_tickets`, `get_ticket`, `create_ticket`,
  `update_ticket`, `add_ticket_comment` (public/internal flag), `list_tickets`,
  `get_user`, `search_users`, `list_macros`, `apply_macro`.
- **Risks:** public vs internal comments must be explicit (avoid accidentally replying to
  customers). Rate limits — handle 429 with backoff.

## 3. Calendly — external scheduling  ·  Effort: S

**Persona:** exec assistant, SDR. **Pairs with:** Google Calendar, HubSpot.

- **Auth:** personal access token (Bearer) — simplest. OAuth optional later.
- **API:** Calendly API v2 (REST/JSON).
- **Config pattern to copy:** Asana single-token.
- **Proposed tools (≈7):** `get_current_user`, `list_event_types`,
  `create_scheduling_link` (single-use booking link), `list_scheduled_events`,
  `get_scheduled_event`, `list_event_invitees`, `cancel_event`.
- **Note / limitation:** the actual booking happens on Calendly's hosted page — the API
  surfaces event types, generates single-use scheduling links, and reads/cancels booked
  events. It does not create a booking directly. Set expectations in tool descriptions.
- **Risks:** low. Mostly read + link generation.

## 4. QuickBooks Online — bookkeeping  ·  Effort: M/L

**Persona:** bookkeeper / accountant (the highest-cost recurring SMB role). **Pairs with:**
Stripe.

- **Auth:** OAuth 2.0 authorization-code + refresh token (Intuit), plus a **Realm ID**
  (company ID). **Reuse the GoogleWorkspace `[OAuthProvider]` + refresh-token pattern** — add
  a `RealmId` config field alongside the OAuth credentials.
- **API:** QuickBooks Online Accounting API v3 (REST/JSON). Prefer **raw `HttpClient`** over
  the dated `IppDotNetSdkForQuickBooksApiV3` SDK. **Sandbox** company available for dev.
- **Config pattern to copy:** GoogleWorkspace (OAuth credentials record) + extra `RealmId`.
- **Proposed tools (≈12):** `list_customers`, `create_customer`, `create_invoice`,
  `send_invoice`, `list_invoices`, `list_expenses`, `create_expense`,
  `categorize_transaction`, `list_accounts` (chart of accounts), `list_vendors`,
  `create_bill`, `run_report` (P&L, balance sheet, A/R aging).
- **Risks:** broad entity surface — scope v1 to invoices + customers + expenses + reports,
  defer payroll/journal entries. Write operations should support HQ confirmation flow.
  Token refresh + Realm ID handling needs care.

## 5. DocuSign — e-signature  ·  Effort: M/L

**Persona:** sales/SDR, ops, legal. **Pairs with:** HubSpot, Google Workspace/M365 (source
documents).

- **Auth:** OAuth 2.0 **JWT grant** (server-to-server): integration key + RSA private key +
  impersonated user GUID + account ID. This is the main cost of the plugin. Config fields:
  `IntegrationKey`, `UserId`, `AccountId`, `PrivateKey`, `BasePath` (demo vs prod).
- **SDK:** official `DocuSign.eSign` NuGet.
- **Config pattern to copy:** new auth shape (no existing JWT-grant plugin) — closest is the
  credential-record approach.
- **Proposed tools (≈7):** `send_envelope` (document bytes + signer list),
  `send_envelope_from_template`, `get_envelope_status`, `list_envelopes`,
  `list_recipients`, `download_completed_document`, `void_envelope`.
- **Risks:** JWT consent (one-time admin consent grant) is a setup gotcha — document it.
  Demo (`demo.docusign.net`) vs production base path must be a config field. Sending for
  signature is outward-facing — route through confirmation flow.

---

## Deferred (next wave, not in this roadmap)

Shopify (e-commerce manager) · Mailchimp + social posting (marketer) · GitHub/GitLab
(engineer) · SQL/BigQuery + Google Analytics (data analyst) · Gusto/BambooHR (HR) ·
Notion/Confluence (company wiki).
