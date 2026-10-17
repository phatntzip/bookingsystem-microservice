using Anthropic.Models.Messages;
using BookingSystem.AiOrchestration.Chat;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

namespace AiOrchestration.Tests;

public class PromptCachingTests
{
    private static readonly PromptCacheOptions On = new();

    // GetCacheControl is internal to the Anthropic SDK, so read the marker back via reflection.
    private static readonly System.Reflection.MethodInfo GetCacheControlMethod =
        typeof(Anthropic.AnthropicClient).Assembly
            .GetType("Microsoft.Extensions.AI.AIContentCacheExtensions")!
            .GetMethod("GetCacheControl", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!;

    private static CacheControlEphemeral? Marker(AIContent content) =>
        (CacheControlEphemeral?)GetCacheControlMethod.Invoke(null, [content]);

    private static int MarkerCount(IEnumerable<ChatMessage> history) =>
        history.SelectMany(m => m.Contents).Count(c => Marker(c) is not null);

    [Fact]
    public void SystemMessage_WhenEnabled_CarriesConfiguredTtl()
    {
        var message = PromptCaching.SystemMessage("prompt", On);

        message.Role.Should().Be(ChatRole.System);
        Marker(message.Contents.Single())!.Ttl!.Value().Should().Be(Ttl.Ttl1h);
    }

    [Fact]
    public void SystemMessage_WhenDisabled_HasNoMarker()
    {
        var message = PromptCaching.SystemMessage("prompt", On with { Enabled = false });

        Marker(message.Contents.Single()).Should().BeNull();
    }

    [Fact]
    public void MarkTail_MarksOnlyTheLastMessage_AndKeepsTheSystemMarker()
    {
        var history = new List<ChatMessage>
        {
            PromptCaching.SystemMessage("prompt", On),
            new(ChatRole.User, "first"),
            new(ChatRole.Assistant, "reply"),
            new(ChatRole.User, "second"),
        };

        PromptCaching.MarkTail(history, On);

        Marker(history[0].Contents.Single()).Should().NotBeNull();
        Marker(history[1].Contents.Single()).Should().BeNull();
        Marker(history[2].Contents.Single()).Should().BeNull();
        Marker(history[3].Contents.Single())!.Ttl!.Value().Should().Be(Ttl.Ttl5m);
    }

    [Fact]
    public void MarkTail_AcrossManyTurns_NeverExceedsTwoBreakpoints()
    {
        var history = new List<ChatMessage> { PromptCaching.SystemMessage("prompt", On) };

        for (var turn = 0; turn < 10; turn++)
        {
            history.Add(new ChatMessage(ChatRole.User, $"question {turn}"));
            PromptCaching.MarkTail(history, On);
            history.Add(new ChatMessage(ChatRole.Assistant, $"answer {turn}"));

            MarkerCount(history).Should().Be(2); // system + tail; the API limit is 4
        }
    }

    [Fact]
    public void MarkTail_WhenDisabled_LeavesHistoryUntouched()
    {
        var history = new List<ChatMessage>
        {
            PromptCaching.SystemMessage("prompt", On with { Enabled = false }),
            new(ChatRole.User, "hello"),
        };

        PromptCaching.MarkTail(history, On with { Enabled = false });

        MarkerCount(history).Should().Be(0);
    }

    [Theory]
    [InlineData("5m", Ttl.Ttl5m)]
    [InlineData("1h", Ttl.Ttl1h)]
    [InlineData("bogus", Ttl.Ttl1h)] // falls back to the default
    public void From_ParsesSystemTtl(string value, Ttl expected)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Anthropic:PromptCache:SystemTtl"] = value })
            .Build();

        PromptCacheOptions.From(config).SystemTtl.Should().Be(expected);
    }
}
