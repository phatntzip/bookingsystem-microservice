using System.Collections.Concurrent;
using Microsoft.Extensions.AI;

namespace BookingSystem.AiOrchestration.Chat;

/// <summary>A write action the model proposed but that only the confirm step may execute.</summary>
public sealed record PendingAction(
    string Tool,
    string Summary,
    HttpMethod Method,
    string Path,
    string? JsonBody);

/// <summary>
/// Scoped to a single /chat turn. A write tool records its intended action here instead of
/// performing it, so the endpoint can surface it to the user for confirmation.
/// </summary>
public sealed class ProposalCapture
{
    public PendingAction? Pending { get; private set; }

    // Keep the first proposed write of a turn; ignore any further ones.
    public void Propose(PendingAction action) => Pending ??= action;
}

/// <summary>Singleton. Holds proposals awaiting confirmation, keyed by an opaque id.</summary>
public sealed class ProposalStore
{
    private readonly ConcurrentDictionary<string, PendingAction> _pending = new();

    public string Add(PendingAction action)
    {
        var id = Guid.NewGuid().ToString("N");
        _pending[id] = action;
        return id;
    }

    public bool TryTake(string id, out PendingAction action) => _pending.TryRemove(id, out action!);
}

/// <summary>Singleton. In-memory conversation histories keyed by conversationId (Phase 1 store).</summary>
public sealed class ConversationStore
{
    private readonly ConcurrentDictionary<string, List<ChatMessage>> _conversations = new();

    public List<ChatMessage> GetOrCreate(string conversationId, Func<IEnumerable<ChatMessage>> seed) =>
        _conversations.GetOrAdd(conversationId, _ => seed().ToList());
}

/// <summary>Executes a confirmed proposal against the gateway. This is the "code executes" half.</summary>
public static class ProposalExecutor
{
    public static async Task<string> ExecuteAsync(HttpClient gateway, PendingAction action, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(action.Method, action.Path);
        if (action.JsonBody is not null)
            request.Content = new StringContent(action.JsonBody, System.Text.Encoding.UTF8, "application/json");

        using var resp = await gateway.SendAsync(request, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        return resp.IsSuccessStatusCode
            ? $"Success ({(int)resp.StatusCode}). {body}"
            : $"Failed ({(int)resp.StatusCode}). {body}";
    }
}
