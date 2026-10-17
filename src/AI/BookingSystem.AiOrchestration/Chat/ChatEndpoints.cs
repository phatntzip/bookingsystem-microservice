using BookingSystem.AiOrchestration.Tools;
using Microsoft.Extensions.AI;

namespace BookingSystem.AiOrchestration.Chat;

public sealed record ChatRequest(string Message, string? ConversationId);
public sealed record ConfirmRequest(string ConversationId, string ProposalId, bool Approve);

public static class ChatEndpoints
{
    public static void MapChatEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/chat");

        // Interpret a natural-language message, run read tools, and surface any write as a proposal.
        group.MapPost("/", async (
            ChatRequest req,
            IChatClient chat,
            BookingTools tools,
            ProposalCapture capture,
            ProposalStore proposals,
            ConversationStore conversations,
            PromptCacheOptions cacheOptions,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Message))
                return Results.BadRequest(new { error = "Message is required." });

            var conversationId = string.IsNullOrWhiteSpace(req.ConversationId)
                ? Guid.NewGuid().ToString("N")
                : req.ConversationId!;

            var history = conversations.GetOrCreate(
                conversationId,
                () => [PromptCaching.SystemMessage(SystemPrompt.Build(), cacheOptions)]);

            history.Add(new ChatMessage(ChatRole.User, req.Message));
            PromptCaching.MarkTail(history, cacheOptions);

            var options = new ChatOptions
            {
                Tools =
                [
                    AIFunctionFactory.Create(tools.SearchCatalogs),
                    AIFunctionFactory.Create(tools.SearchCatalogsByName),
                    AIFunctionFactory.Create(tools.GetCatalog),
                    AIFunctionFactory.Create(tools.GetBooking),
                    AIFunctionFactory.Create(tools.CreateBooking),
                    AIFunctionFactory.Create(tools.CancelBooking),
                ],
            };

            var response = await chat.GetResponseAsync(history, options, ct);
            history.AddRange(response.Messages);

            // Ground truth that caching works: cached should be > 0 from the second turn on.
            if (response.Usage is { } usage)
            {
                var extra = usage.AdditionalCounts is null
                    ? ""
                    : string.Join(", ", usage.AdditionalCounts.Select(kv => $"{kv.Key}={kv.Value}"));
                loggerFactory.CreateLogger("PromptCache").LogInformation(
                    "Usage: input={Input} cachedInput={Cached} output={Output} {Extra}",
                    usage.InputTokenCount, usage.CachedInputTokenCount, usage.OutputTokenCount, extra);
            }

            if (capture.Pending is { } pending)
            {
                var proposalId = proposals.Add(pending);
                return Results.Ok(new
                {
                    conversationId,
                    assistantMessage = response.Text,
                    proposal = new
                    {
                        proposalId,
                        action = pending.Tool,
                        summary = pending.Summary,
                        requiresConfirmation = true,
                    },
                });
            }

            return Results.Ok(new { conversationId, assistantMessage = response.Text });
        });

        // The confirmation gate: only here does a write actually hit the gateway.
        group.MapPost("/confirm", async (
            ConfirmRequest req,
            IHttpClientFactory httpClientFactory,
            ProposalStore proposals,
            ConversationStore conversations,
            CancellationToken ct) =>
        {
            if (!proposals.TryTake(req.ProposalId, out var action))
                return Results.NotFound(new { error = "Unknown or already-resolved proposal." });

            var history = conversations.GetOrCreate(req.ConversationId, () => []);

            if (!req.Approve)
            {
                history.Add(new ChatMessage(ChatRole.User, $"[The user declined the proposed {action.Tool}.]"));
                return Results.Ok(new { status = "cancelled", action = action.Tool });
            }

            var gateway = httpClientFactory.CreateClient("gateway");
            var result = await ProposalExecutor.ExecuteAsync(gateway, action, ct);
            history.Add(new ChatMessage(ChatRole.User, $"[The user confirmed the {action.Tool}. Outcome: {result}]"));

            return Results.Ok(new { status = "executed", action = action.Tool, result });
        });
    }
}
