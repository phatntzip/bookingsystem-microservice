# AI Orchestration Improvement Plan

> **Service:** `BookingSystem.AiOrchestration`
> **Created:** 2026-10-16
> **Updated:** 2026-10-17 — aligned with #48 (AI platform foundation) and #49 (DLQ triage agent)
> **Status:** Draft

---

## Overview

The current `AiOrchestration` service provides a functional, read/write-separated AI chat layer over the booking gateway, backed by Claude via `Microsoft.Extensions.AI`. This plan captures eight improvement dimensions to make it smarter, more robust, and production-ready.

Two newer issues change the shape of the plan. **#48** moves the model client, telemetry, caching, rate limiting and guardrails into a shared `BookingSystem.Shared.AI` project, so several items that were planned as one-off work in this service (resilience, rate limiting, telemetry, model routing) are now either delivered by the platform or must be built on top of it. **#49** adds a second AI consumer (`BookingSystem.OpsAgent`) that uses the same "model proposes, deterministic code executes, human approves" rule, so the proposal and confirmation design here should stay generic enough to be reused.

## Related Issues

| Issue | Title | Effect on this plan |
|---|---|---|
| #27 | Add AI assistant | Parent feature. Its phases (single action → multi-step → memory → knowledge lookup) map to Improvements 2, 3, 4 and 1. |
| #48 | AI platform foundation (`Shared.AI`) | **Prerequisite.** Replaces the hand-built `AnthropicClient` block in `Program.cs`. Absorbs the platform parts of Improvements 5 and 6. See Improvement 7. |
| #49 | AI-orchestrated DLQ triage (`OpsAgent`) | Second consumer of `Shared.AI` and of the proposal-then-approve pattern. Constrains the design of Improvement 3. See Improvement 8. |
| #28 | Keycloak authentication | **Prerequisite** for the JWT-based `userId` in Improvement 5. Until it lands, `DemoUserId` stays. |
| #35 | Validate user exists before booking | Server-side check that backs up the assistant's `CreateBooking` proposal. No work in this service, but the tool must surface the resulting `NotFoundException` clearly. |
| #40 | Trace context across Kafka | The AI spans from #48 and the tool-call spans here should join the same trace once the gateway and services propagate context. |
| #41 | Deep health checks | Add a `ready` check for the model provider and the gateway once #41 lands. |

---

## Current Architecture (Baseline)

```
User → POST /chat
         ↓
   ConversationStore (in-memory history)
         ↓
   Claude (claude-opus-4-8) via Microsoft.Extensions.AI
         ↓
   BookingTools (6 tools: 4 reads, 2 writes-as-proposals)
         ↓
   ProposalCapture (write interceptor, max 1 proposal/turn)
         ↓
   Response → optional ProposalStore entry
         ↓
User → POST /chat/confirm (approve/decline)
         ↓
   ProposalExecutor → Gateway HTTP call
```

**What's good:**
- Clean read/write separation with a human-in-the-loop confirmation gate
- Provider-swappable `IChatClient` abstraction (OpenAI, Azure OpenAI, Ollama drop-in)
- Minimal, focused surface area

**What's missing:** See the improvements below.

---

## Improvement 1 — Smarter System Prompt

**File:** `Chat/SystemPrompt.cs`
**Effort:** Low | **Impact:** High

### Problem
The system prompt is a static 6-line string. It gives the model almost no domain context, leading to more round-trips and less accurate responses.

### Plan

- Accept `UserContext` and top catalog summaries as inputs to `Build()`
- Inject the user's name and active booking count for personalization
- Embed booking domain rules (min/max stay, cancellation policy)
- Embed top N catalogs at session start to reduce unnecessary `SearchCatalogs` calls
- Specify a structured output format so the frontend can render rich cards
- Add a graceful-degradation instruction ("if you cannot complete the request with available tools, say so clearly")

### Target API

```csharp
public static string Build(UserContext user, CatalogSummary[] topCatalogs)
{
    return $"""
        You are a concierge booking assistant for {user.Name}.
        Today is {DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}.
        The user has {user.ActiveBookingCount} active booking(s).

        Booking rules:
        - Minimum stay: 1 night. Maximum stay: 30 nights.
        - Bookings may only start from today onwards.
        - Cancellation is free up to 48 hours before check-in.

        Popular catalogs to recommend if the user seems undecided:
        {string.Join("\n", topCatalogs.Select(c => $"- {c.Name} (${c.PricePerNight}/night, {c.Location})"))}

        Read tools execute immediately. Write tools (CreateBooking, CancelBooking) only PREPARE
        a proposal — never say a booking was made. Keep replies concise and factual.
        When surfacing a catalog, always include its ID, name, price, and availability dates.
        If you cannot complete the request with available tools, say so clearly.
        """;
}
```

---

## Improvement 2 — Richer Tool Surface

**File:** `Tools/BookingTools.cs`
**Effort:** Low | **Impact:** High

### Problem
Only 6 tools exist (4 reads, 2 write-proposals). Common user intents like "show my bookings" or "is this available for next week?" have no tool to satisfy them, forcing the model to either hallucinate or refuse.

### Current Tools

| Tool | Type |
|---|---|
| `SearchCatalogs` | Read |
| `SearchCatalogsByName` | Read |
| `GetCatalog` | Read |
| `GetBooking` | Read |
| `CreateBooking` | Write (proposal) |
| `CancelBooking` | Write (proposal) |

### New Tools to Add

| Tool | Type | Description |
|---|---|---|
| `ListMyBookings` | Read | List all bookings for the current user, with optional status filter |
| `CheckAvailability` | Read | Validate that a catalog has availability for specific dates before proposing |
| `GetPricingEstimate` | Read | Return total cost (nights × price) so the user knows before confirming |
| `UpdateBooking` | Write (proposal) | Modify check-in/out dates without a cancel-then-rebook flow |
| `GetCatalogReviews` | Read | Surface ratings and reviews to aid decision-making |
| `SearchNearby` | Read | Location-aware search, e.g. "find something near Da Nang" |

### Example Implementations

```csharp
[Description("List all bookings for the current user. Read-only; runs immediately.")]
public async Task<string> ListMyBookings(
    [Description("Status filter: 'active', 'cancelled', or 'all'. Default 'all'.")] string status = "all")
{
    var resp = await Gateway.GetAsync($"/api/bookings?userId={_userId}&status={status}");
    var body = await resp.Content.ReadAsStringAsync();
    return resp.IsSuccessStatusCode ? body : $"Failed ({(int)resp.StatusCode}). {body}";
}

[Description("Check if a catalog is available for given dates. Read-only; runs immediately.")]
public async Task<string> CheckAvailability(
    [Description("The catalog GUID.")] Guid catalogId,
    [Description("Check-in date, ISO format yyyy-MM-dd.")] string checkIn,
    [Description("Check-out date, ISO format yyyy-MM-dd.")] string checkOut)
{
    var resp = await Gateway.GetAsync(
        $"/api/catalog/catalogs/{catalogId}/availability?checkIn={checkIn}&checkOut={checkOut}");
    var body = await resp.Content.ReadAsStringAsync();
    return resp.IsSuccessStatusCode ? body : $"Availability check failed ({(int)resp.StatusCode}). {body}";
}
```

---

## Improvement 3 — Multi-Proposal & Richer Confirmation Flow

**File:** `Chat/OrchestrationState.cs`, `Chat/ChatEndpoints.cs`
**Effort:** Low | **Impact:** Medium

### Problem

`ProposalCapture.Propose()` silently drops any proposal after the first one per turn:

```csharp
// Current — drops second and subsequent proposals
public void Propose(PendingAction action) => Pending ??= action;
```

Also, proposals have no expiry and the response shape carries only minimal information for the frontend to render.

### Plan

#### a) Support Multiple Proposals per Turn

```csharp
public sealed class ProposalCapture
{
    private readonly List<PendingAction> _proposals = [];
    public IReadOnlyList<PendingAction> Pending => _proposals;
    public void Propose(PendingAction action) => _proposals.Add(action);
}
```

This enables flows like: "Cancel booking A and rebook catalog B for the same dates."

#### b) Add Proposal Expiry (TTL)

```csharp
public sealed record PendingAction(
    string Tool,
    string Summary,
    HttpMethod Method,
    string Path,
    string? JsonBody,
    DateTimeOffset ExpiresAt);
```

Stale proposals are removed on access, preventing unbounded memory growth.

#### c) Richer Proposal Response Shape

```json
{
  "conversationId": "...",
  "assistantMessage": "I've prepared a booking for you.",
  "proposals": [
    {
      "proposalId": "...",
      "action": "CreateBooking",
      "summary": "Book Seaside Villa, 3 nights",
      "details": {
        "catalogId": "...",
        "checkIn": "2026-11-01",
        "checkOut": "2026-11-04",
        "estimatedTotal": 450
      },
      "requiresConfirmation": true,
      "expiresAt": "2026-10-17T10:00:00Z"
    }
  ]
}
```

---

## Improvement 4 — Persistent Conversation Memory

**File:** `Chat/OrchestrationState.cs`, `Program.cs`
**Effort:** Medium | **Impact:** Medium

### Problem

`ConversationStore` is an unbounded in-memory `ConcurrentDictionary`. It is:
- Lost on every process restart
- Unbounded — grows forever with no eviction
- Not shared across scaled-out instances

### Plan

#### a) Redis-Backed Store (for Aspire / distributed scenarios)

```csharp
// Program.cs
builder.AddRedisClient("cache");
builder.Services.AddSingleton<ConversationStore, RedisConversationStore>();
```

```csharp
public sealed class RedisConversationStore(IConnectionMultiplexer redis) : ConversationStore
{
    // Serialize/deserialize ChatMessage lists with a sliding 24h TTL
}
```

#### b) Sliding-Window Context Trimming

Long histories inflate cost and eventually exceed the model's context window.

```csharp
public static List<ChatMessage> Trim(List<ChatMessage> history, int maxMessages = 40)
{
    if (history.Count <= maxMessages) return history;

    var system = history.First();
    var recent = history.Skip(history.Count - (maxMessages - 1)).ToList();
    var placeholder = new ChatMessage(ChatRole.System,
        $"[Earlier conversation trimmed. {history.Count - maxMessages} message(s) omitted.]");

    return [system, placeholder, ..recent];
}
```

#### c) AI-Powered Summarization (Advanced)

Every N turns, ask the model to summarize older messages and replace them with a rolling summary `ChatMessage`, preserving semantic context without burning tokens.

---

## Improvement 5 — Robustness & Production Hardening

**Files:** `Program.cs`, `Tools/BookingTools.cs`, `Chat/ChatEndpoints.cs`
**Effort:** Low–Medium | **Impact:** High

### Current Gaps

| Issue | Risk | Fix |
|---|---|---|
| No retry on transient HTTP failures to the gateway | Tool calls fail silently on flaky network | `.AddStandardResilienceHandler()` on the gateway `HttpClient` |
| `userId` hardcoded to `DemoUserId` from config | All users share one identity | Read from JWT claim via `IHttpContextAccessor`. **Blocked on #28** (Keycloak) |
| No rate limiting on `/chat` | Cost explosion and abuse | Per-user token bucket on the HTTP endpoint (`AddRateLimiter()`). The per-service LLM-call limit comes from `Shared.AI` (#48), so the two limits do not overlap |
| No streaming | Feels slow for long answers | `GetStreamingResponseAsync()` + SSE endpoint |
| No telemetry on tool calls | Zero observability into AI behavior | LLM spans and token metrics come from `Shared.AI` (#48). This service adds only a span and a log event per **tool call** (tool name, duration, outcome) |

### Resilient Gateway Client

```csharp
builder.Services
    .AddHttpClient("gateway", c => c.BaseAddress = new Uri(gatewayUrl))
    .AddStandardResilienceHandler(); // retries, circuit breaker, timeout
```

### Streaming SSE Endpoint

```csharp
group.MapGet("/stream", async (
    string message, string? conversationId,
    IChatClient chat, BookingTools tools,
    ConversationStore conversations,
    HttpContext ctx, CancellationToken ct) =>
{
    ctx.Response.ContentType = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";

    await foreach (var update in chat.GetStreamingResponseAsync(history, options, ct))
    {
        await ctx.Response.WriteAsync($"data: {update.Text}\n\n", ct);
        await ctx.Response.Body.FlushAsync(ct);
    }
});
```

### JWT-Based User Identity

```csharp
// In BookingTools — replace hardcoded DemoUserId
private Guid UserId => Guid.Parse(
    _httpContextAccessor.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)
    ?? throw new InvalidOperationException("User is not authenticated."));
```

---

## Improvement 6 — Smart Model Routing

**File:** `Program.cs`
**Effort:** Medium | **Impact:** Medium

### Problem

Every request — whether a simple "show my bookings" or a complex multi-catalog comparison — is sent to `claude-opus-4-8`. This is expensive and unnecessarily slow for simple queries.

> **Depends on #48.** Once `Shared.AI` owns client construction, routing is a middleware or keyed-client concern on top of it, and the model names come from `AI:*` configuration, not string literals in `Program.cs`. Build this after Improvement 7. Note that #48's provider switch (Ollama / Azure OpenAI / OpenAI / Anthropic) means the haiku/opus pair below only applies when the provider is Anthropic.

### Plan

Route based on a heuristic (conversation turn count, number of active tools, message complexity):

```
Simple (short history, single-tool intent)  → claude-haiku-4-5  (fast, cheap)
Complex (multi-step, long history, writes)  → claude-opus-4-8   (powerful, accurate)
```

Implement as a custom `IChatClient` middleware:

```csharp
IChatClient chatClient = anthropic
    .AsIChatClient(model, maxTokens)
    .AsBuilder()
    .Use(next => async (messages, options, ct) =>
    {
        var isComplex = messages.Count > 10 || options?.Tools?.Count > 3;
        var routedModel = isComplex ? "claude-opus-4-8" : "claude-haiku-4-5";
        var routedOptions = (options ?? new ChatOptions()) with { ModelId = routedModel };
        return await next(messages, routedOptions, ct);
    })
    .UseFunctionInvocation()
    .Build();
```

---

## Improvement 7 — Adopt the `Shared.AI` Platform (#48)

**Files:** `Program.cs`, `BookingSystem.AiOrchestration.csproj`, `appsettings.json`, `AppHost/Program.cs`
**Effort:** Medium | **Impact:** High | **Depends on:** #48

### Problem

`Program.cs` builds an `AnthropicClient` by hand, reads `Anthropic:ApiKey` / `ANTHROPIC_API_KEY` itself, and hardcodes the model name and token limit. Each AI service that follows (#49 `OpsAgent`, later semantic search or review summaries) would repeat that setup, along with its own secrets handling and telemetry. #48 provides the shared version.

### Plan

- Replace the manual client block with `builder.AddBookingSystemChatClient("ai")` from `BookingSystem.Shared.AI`.
- Keep `.UseFunctionInvocation()` here. Tool invocation is specific to this service and stays out of the shared pipeline.
- Drop the `Anthropic:*` settings in favour of `AI:Provider` and the `ai` connection injected by the AppHost (`.WithReference(ai)`). The API key becomes an AppHost secret parameter. No keys in `appsettings.json`.
- Register `ai-orchestration` against the new `ai` resource in `AppHost/Program.cs`.
- Remove from this plan anything the platform now provides: LLM spans and token/cost metrics, the per-service LLM rate limit, retry/timeout on model calls, response caching, and the token budget guardrail.
- Use the structured-output guardrail from #48 for any response the frontend renders as cards (see the response shape in Improvement 3).
- **Anthropic prompt caching is already implemented locally** (`Chat/PromptCaching.cs`, config `Anthropic:PromptCache`): one breakpoint on the system message (caches tools + system) and one that moves to the newest message each turn. This is provider-side caching of the *prompt prefix* and is separate from #48's Redis response cache. Keep it when moving to `Shared.AI`: other providers simply ignore the markers, but check that the shared pipeline does not rebuild or reorder `ChatMessage` contents before they reach the Anthropic client. Tool schemas must stay deterministic (same order every request) or the whole prefix misses.
- Keep one consideration in mind when adopting the response cache: **never cache turns that involve tool calls or user-specific state**. A cached "your bookings" answer served to another user would be a data leak. Confirm with #48 which calls opt in to caching.

### Target

```csharp
builder.AddServiceDefaults();
builder.AddBookingSystemChatClient("ai");   // provider, telemetry, cache, limits from Shared.AI

builder.Services.AddChatClient(sp => sp.GetRequiredService<IChatClient>())
    .UseFunctionInvocation();               // service-specific, stays local
```

### Acceptance

- `Program.cs` no longer references `AnthropicClient` or reads an API key.
- Switching `AI:Provider` between Ollama and a hosted provider requires no code change here, and the existing `BookingToolsTests` / `GateStateTests` still pass with the fake `IChatClient`.
- Chat turns show up as GenAI spans and token metrics in the Aspire dashboard.

---

## Improvement 8 — Keep the Proposal Gate Reusable (#49)

**Files:** `Chat/OrchestrationState.cs`, `Chat/ChatEndpoints.cs`
**Effort:** Low | **Impact:** Medium

### Context

#49 adds `BookingSystem.OpsAgent`, which classifies dead-lettered Kafka messages and *proposes* replay, discard or escalate, with a human approving before anything is republished. It follows the same rule as this service: the model proposes, deterministic code executes, and a person approves. That makes the proposal flow a pattern, not a one-off, but the two services differ in important ways:

| | `AiOrchestration` (chat) | `OpsAgent` (#49) |
|---|---|---|
| Proposal store | In-memory (`ProposalStore`), TTL (Improvement 3) | Persistent: `dlq_items` table in `opsdb` |
| Approver | The end user, in the chat session | An operator, via `/api/ops/dlq/{id}/approve` |
| Executor | HTTP call to the gateway | Kafka republish |
| Model output | Free text plus tool calls | Schema-validated `TriageProposal` |

### Plan

- **Do not** move the proposal store into `Shared.*`. The storage, approver and executor all differ, and `architecture.md` forbids business logic in shared projects. Share the *rules* in docs instead.
- Keep `PendingAction` and `ProposalCapture` generic in shape (tool name, human-readable summary, parameters, expiry) so the Improvement 3 response shape could be reused by an ops UI later. Avoid fields that only make sense for HTTP if the executor is later swapped.
- Make execution **idempotent**: approving the same `proposalId` twice must run the action once. #49 makes this an acceptance criterion for replays; apply the same rule to `/chat/confirm` (a double click or retry must not book twice).
- Record the approver and outcome on each confirmed or declined proposal (log event now, persisted once Improvement 4 adds Redis or a database). This matches the audit trail #49 stores in `dlq_items`.
- Add the shared guidance (propose/execute split, confirm gate by risk, idempotent execution, fake `IChatClient` in tests) to `.claude/rules/ai.md`, which #48 already requires. This service and `OpsAgent` should both point to it.
- Optionally expose AI-orchestration tools to the ops flow later (for example, a read-only `GetBooking` lookup). Do not do this until #49's own tool set is defined.

---

## Implementation Priority

Items are ordered by dependency first, then by impact. "Blocked on" means do not start before that item or issue is done.

| # | Improvement | Effort | Impact | Blocked on | Status |
|---|---|---|---|---|---|
| 1 | Adopt `Shared.AI` (replace manual `AnthropicClient`) | Medium | High | #48 | ⬜ Planned |
| 2 | Enrich system prompt with user + domain context | Low | High | — | ⬜ Planned |
| 3 | Add `ListMyBookings` + `CheckAvailability` tools | Low | High | — | ⬜ Planned |
| 4 | HTTP resilience on gateway client | Low | High | — | ⬜ Planned |
| 5 | Idempotent `/chat/confirm` | Low | High | — | ⬜ Planned |
| 6 | Fix silent proposal drop (allow multiple) + TTL | Low | Medium | — | ⬜ Planned |
| 7 | Rate limiting on `/chat` (per-user, HTTP level) | Low | High | — | ⬜ Planned |
| 8 | JWT-based userId (remove `DemoUserId`) | Medium | High | #28 | ⬜ Planned |
| 9 | Sliding-window conversation trimming | Medium | Medium | — | ⬜ Planned |
| 10 | Streaming responses via SSE | Medium | Medium | — | ⬜ Planned |
| 11 | Redis-backed conversation store | Medium | Medium | #48 (`ai:*` Redis namespace) | ⬜ Planned |
| 12 | Smart model routing (haiku vs. opus) | Medium | Medium | #48 | ⬜ Planned |
| 13 | Add `UpdateBooking` + `GetPricingEstimate` tools | Medium | Medium | Booking date amendment (#18) | ⬜ Planned |
| 14 | Tool-call spans, link into Kafka-propagated traces | Low | Medium | #40 | ⬜ Planned |
| 15 | Model-provider and gateway `ready` health checks | Low | Low | #41 | ⬜ Planned |
| 16 | AI-powered conversation summarization | High | Low | — | ⬜ Planned |

Removed from the earlier list because #48 now owns them: LLM-level telemetry, the per-call token budget, and the model-call retry policy.

---

## Files Affected

| File | Changes |
|---|---|
| `Chat/SystemPrompt.cs` | Accept user context + catalog summaries |
| `Chat/OrchestrationState.cs` | Multi-proposal list, TTL, generic `PendingAction`, idempotent execution |
| `Chat/ChatEndpoints.cs` | Multi-proposal response shape, idempotent confirm, streaming endpoint, rate limiter |
| `Tools/BookingTools.cs` | New tools, JWT-based userId, tool-call spans |
| `Program.cs` | `AddBookingSystemChatClient("ai")`, resilient gateway client, rate limiter, model router |
| `appsettings.json` | Remove `Anthropic:*`, add `AI:*` provider config |
| `BookingSystem.AiOrchestration.csproj` | Reference `BookingSystem.Shared.AI`; add `Microsoft.Extensions.Http.Resilience`, `StackExchange.Redis` |
| `AppHost/Program.cs` | `.WithReference(ai)` on `ai-orchestration` (resource itself added by #48) |
| `.claude/rules/ai.md` | Shared propose/execute/confirm guidance (created by #48, extended here) |
| `tests/AiOrchestration.Tests/` | Idempotent confirm, multi-proposal, TTL expiry, `Shared.AI` wiring with a fake `IChatClient` |
