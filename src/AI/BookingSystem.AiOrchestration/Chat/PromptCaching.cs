using Anthropic.Models.Messages;
using Microsoft.Extensions.AI;

namespace BookingSystem.AiOrchestration.Chat;

/// <summary>
/// Anthropic prompt-caching settings. Bound from <c>Anthropic:PromptCache</c>.
/// </summary>
public sealed record PromptCacheOptions(
    bool Enabled = true,
    Ttl SystemTtl = Ttl.Ttl1h,
    Ttl TailTtl = Ttl.Ttl5m)
{
    public static PromptCacheOptions From(IConfiguration config)
    {
        var section = config.GetSection("Anthropic:PromptCache");
        return new PromptCacheOptions(
            section.GetValue("Enabled", true),
            ParseTtl(section["SystemTtl"], Ttl.Ttl1h),
            ParseTtl(section["TailTtl"], Ttl.Ttl5m));
    }

    private static Ttl ParseTtl(string? value, Ttl fallback) => value switch
    {
        "5m" => Ttl.Ttl5m,
        "1h" => Ttl.Ttl1h,
        _ => fallback,
    };
}

/// <summary>
/// Places <c>cache_control</c> breakpoints on the prompt. Caching is a prefix match and the request
/// renders tools → system → messages, so two breakpoints cover this service:
/// <list type="number">
///   <item>the system message — caches the (deterministic) tool list and the system prompt together;</item>
///   <item>the last message of the history — caches the conversation so far, moving forward each turn.</item>
/// </list>
/// Only these two are ever set (the API allows 4), and the 1h system entry must precede the 5m tail.
/// Only the <c>IChatClient</c> from <c>AsIChatClient()</c> honours these markers; other providers ignore them.
/// </summary>
public static class PromptCaching
{
    public static ChatMessage SystemMessage(string text, PromptCacheOptions options)
    {
        var content = new TextContent(text);
        if (options.Enabled) content.WithCacheControl(options.SystemTtl);
        return new ChatMessage(ChatRole.System, [content]);
    }

    /// <summary>
    /// Moves the tail breakpoint onto the newest message. Markers set on earlier turns are removed first,
    /// otherwise they accumulate across turns and the request would exceed the 4-breakpoint limit.
    /// Markers are not part of the cache key, so clearing them does not invalidate existing entries.
    /// </summary>
    public static void MarkTail(IReadOnlyList<ChatMessage> history, PromptCacheOptions options)
    {
        if (!options.Enabled) return;

        foreach (var message in history)
        {
            if (message.Role == ChatRole.System) continue;
            foreach (var content in message.Contents)
                content.WithCacheControl((CacheControlEphemeral?)null);
        }

        var last = history.LastOrDefault(m => m.Role != ChatRole.System);
        last?.Contents.LastOrDefault()?.WithCacheControl(options.TailTtl);
    }
}
