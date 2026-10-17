using System.Net;
using BookingSystem.AiOrchestration.Chat;
using FluentAssertions;

namespace AiOrchestration.Tests;

public class ProposalStoreTests
{
    private readonly ProposalStore _store = new();

    [Fact]
    public void Add_ThenTake_RoundTripsTheAction()
    {
        var action = new PendingAction("CreateBooking", "s", HttpMethod.Post, "/api/bookings", "{}");

        var id = _store.Add(action);
        var taken = _store.TryTake(id, out var result);

        taken.Should().BeTrue();
        result.Should().Be(action);
    }

    [Fact]
    public void TryTake_SameProposalTwice_SecondReturnsFalse()
    {
        var id = _store.Add(new PendingAction("CancelBooking", "s", HttpMethod.Delete, "/api/bookings/1", null));

        _store.TryTake(id, out _).Should().BeTrue();
        _store.TryTake(id, out _).Should().BeFalse(); // cannot confirm the same proposal twice
    }

    [Fact]
    public void TryTake_UnknownId_ReturnsFalse()
    {
        _store.TryTake("nope", out _).Should().BeFalse();
    }
}

public class ProposalExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_SendsStoredMethodPathAndBody()
    {
        var handler = new StubHandler(HttpStatusCode.Created, """{"id":"abc"}""");
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://gateway.local") };
        var action = new PendingAction("CreateBooking", "s", HttpMethod.Post, "/api/bookings", """{"catalogId":"x"}""");

        var result = await ProposalExecutor.ExecuteAsync(client, action, default);

        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.RequestUri!.PathAndQuery.Should().Be("/api/bookings");
        handler.LastRequestBody.Should().Be("""{"catalogId":"x"}""");
        result.Should().StartWith("Success (201).");
    }

    [Fact]
    public async Task ExecuteAsync_NonSuccess_ReportsFailureWithBody()
    {
        var handler = new StubHandler(HttpStatusCode.BadRequest, "bad request");
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://gateway.local") };
        var action = new PendingAction("CancelBooking", "s", HttpMethod.Delete, "/api/bookings/1?reason=x", null);

        var result = await ProposalExecutor.ExecuteAsync(client, action, default);

        result.Should().Be("Failed (400). bad request");
    }
}
