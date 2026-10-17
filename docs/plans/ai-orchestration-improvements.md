# AI Orchestration Improvement Plan

> **Service:** `BookingSystem.AiOrchestration`
> **Created:** 2026-10-16
> **Status:** Draft

---

## Overview

The current `AiOrchestration` service provides a functional, read/write-separated AI chat layer over the booking gateway, backed by Claude via `Microsoft.Extensions.AI`. This plan captures six improvement dimensions to make it smarter, more robust, and production-ready.

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
| `userId` hardcoded to `DemoUserId` from config | All users share one identity | Read from JWT claim via `IHttpContextAccessor` |
| No rate limiting on `/chat` | Cost explosion and abuse | `AddRateLimiter()` with per-user token bucket |
| No streaming | Feels slow for long answers | `GetStreamingResponseAsync()` + SSE endpoint |
| No telemetry on tool calls | Zero observability into AI behavior | Structured `ILogger` events + OpenTelemetry traces per tool |

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

## Implementation Priority

| # | Improvement | Effort | Impact | Status |
|---|---|---|---|---|
| 1 | Enrich system prompt with user + domain context | Low | High | ⬜ Planned |
| 2 | Add `ListMyBookings` + `CheckAvailability` tools | Low | High | ⬜ Planned |
| 3 | HTTP resilience on gateway client | Low | High | ⬜ Planned |
| 4 | JWT-based userId (remove `DemoUserId`) | Medium | High | ⬜ Planned |
| 5 | Rate limiting on `/chat` | Low | High | ⬜ Planned |
| 6 | Fix silent proposal drop (allow multiple) | Low | Medium | ⬜ Planned |
| 7 | Sliding-window conversation trimming | Medium | Medium | ⬜ Planned |
| 8 | Streaming responses via SSE | Medium | Medium | ⬜ Planned |
| 9 | Redis-backed conversation store | Medium | Medium | ⬜ Planned |
| 10 | Smart model routing (haiku vs. opus) | Medium | Medium | ⬜ Planned |
| 11 | Add `UpdateBooking` + `GetPricingEstimate` tools | Medium | Medium | ⬜ Planned |
| 12 | AI-powered conversation summarization | High | Low | ⬜ Planned |

---

## Files Affected

| File | Changes |
|---|---|
| `Chat/SystemPrompt.cs` | Accept user context + catalog summaries |
| `Chat/OrchestrationState.cs` | Multi-proposal list, TTL, richer `PendingAction` |
| `Chat/ChatEndpoints.cs` | Multi-proposal response shape, streaming endpoint, rate limiter |
| `Tools/BookingTools.cs` | New tools, JWT-based userId |
| `Program.cs` | Resilient HTTP client, rate limiter, model router middleware |
| `BookingSystem.AiOrchestration.csproj` | New packages (`Microsoft.Extensions.Http.Resilience`, `StackExchange.Redis`, etc.) |
