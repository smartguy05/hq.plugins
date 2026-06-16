# HQ.Plugins.Perplexity

Exposes [Perplexity](https://docs.perplexity.ai/) Sonar research as two LLM-callable tools.

## Tools

### `perplexity_search` (synchronous)
Fast, cited web search. Returns `{ Success, Answer, Citations }` in the same turn.

| Parameter | Type | Notes |
|---|---|---|
| `query` | string (required) | The question / search query. |
| `recency` | string | `day` \| `week` \| `month` \| `year`. Maps to `search_recency_filter`. |
| `domainFilters` | string[] | Include domains, or exclude with a `-` prefix (`-pinterest.com`). Merged with configured defaults. |
| `model` | string | Override the configured default model (e.g. `sonar`, `sonar-pro`). |

### `perplexity_deep_research` (asynchronous)
Runs `sonar-deep-research` — an exhaustive multi-step investigation that takes **several minutes**.

Because the orchestrator completes the tool turn when the job is *submitted*, this tool returns
immediately with `{ Success, Status: "started" }`. A background task submits the job to Perplexity's
async job API (`POST /v1/async/sonar`), polls it (`GET /v1/async/sonar/{id}`) every 15s until it
completes (or 30 min elapses), then delivers the cited answer back into the **same conversation** as a
new turn, via `IOrchestrator.ProcessRequest` (the same path inbound Slack/Telegram messages use).

| Parameter | Type | Notes |
|---|---|---|
| `query` | string (required) | Be specific — this runs an exhaustive investigation. |
| `recency` | string | As above. |
| `domainFilters` | string[] | As above. |

> **Delivery note:** the result arrives as a *new* turn (the original turn has already ended — the
> orchestrator has no mid-turn injection). For channel-backed conversations (Slack/Telegram) the
> follow-up is pushed back to the chat. For a bare web turn with no live listener, it lands in
> conversation history and surfaces on the next interaction. If no conversation id is available, the
> tool falls back to running synchronously and returns the answer inline.

## Configuration (`ServiceConfig`)

| Field | Notes |
|---|---|
| `PerplexityApiKey` | API key (starts with `pplx-`). |
| `DefaultSearchModel` | Default model for `perplexity_search`. Defaults to `sonar-pro`. |
| `DefaultDomainFilters` | Domain filters merged into every call. |
| `MaxTokens` | Optional `max_tokens` cost ceiling. |
| `AiPlugin` | Optional override for async result routing. Defaults to the calling service. |
| `AgentId` | Injected by the host; used to route async results. |

## Build

```bash
dotnet build HQ.Plugins.Perplexity/HQ.Plugins.Perplexity.csproj
```

Output deploys to the host's `Plugins/` directory; HQ discovers it at runtime via `AssemblyLoadContext`.
