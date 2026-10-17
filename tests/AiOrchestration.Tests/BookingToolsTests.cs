using System.Net;
using System.Text.Json;
using BookingSystem.AiOrchestration.Chat;
using FluentAssertions;

namespace AiOrchestration.Tests;

public class BookingToolsTests
{
    private readonly ProposalCapture _capture = new();

    // --- Writes propose, they do not execute (the core gate property) ---

    [Fact]
    public void CreateBooking_ValidInput_CapturesPostProposal_AndDoesNotCallGateway()
    {
        var handler = new StubHandler();
        var demoUser = Guid.NewGuid();
        var catalogId = Guid.NewGuid();
        var tools = TestFactory.Tools(_capture, handler, demoUser);

        var reply = tools.CreateBooking(catalogId, "2026-07-10", "2026-07-12");

        // No HTTP was performed — the write was only proposed.
        handler.LastRequest.Should().BeNull();
        reply.Should().StartWith("Prepared");

        _capture.Pending.Should().NotBeNull();
        _capture.Pending!.Tool.Should().Be("CreateBooking");
        _capture.Pending.Method.Should().Be(HttpMethod.Post);
        _capture.Pending.Path.Should().Be("/api/bookings");

        using var doc = JsonDocument.Parse(_capture.Pending.JsonBody!);
        var root = doc.RootElement;
        root.GetProperty("userId").GetGuid().Should().Be(demoUser);
        root.GetProperty("catalogId").GetGuid().Should().Be(catalogId);
        root.GetProperty("checkIn").GetString().Should().Be("2026-07-10");
        root.GetProperty("checkOut").GetString().Should().Be("2026-07-12");
    }

    [Fact]
    public void CreateBooking_UnparseableDate_ReturnsError_AndCapturesNothing()
    {
        var tools = TestFactory.Tools(_capture, new StubHandler(), Guid.NewGuid());

        var reply = tools.CreateBooking(Guid.NewGuid(), "not-a-date", "2026-07-12");

        reply.Should().Contain("Invalid date");
        _capture.Pending.Should().BeNull();
    }

    [Fact]
    public void CreateBooking_CheckoutNotAfterCheckin_ReturnsError_AndCapturesNothing()
    {
        var tools = TestFactory.Tools(_capture, new StubHandler(), Guid.NewGuid());

        var reply = tools.CreateBooking(Guid.NewGuid(), "2026-07-12", "2026-07-12");

        reply.Should().Contain("Check-out must be after check-in");
        _capture.Pending.Should().BeNull();
    }

    [Fact]
    public void CancelBooking_CapturesDeleteProposal_WithEscapedReason()
    {
        var handler = new StubHandler();
        var bookingId = Guid.NewGuid();
        var tools = TestFactory.Tools(_capture, handler, Guid.NewGuid());

        var reply = tools.CancelBooking(bookingId, "changed my mind");

        handler.LastRequest.Should().BeNull();
        reply.Should().StartWith("Prepared");
        _capture.Pending!.Method.Should().Be(HttpMethod.Delete);
        _capture.Pending.Path.Should().Be($"/api/bookings/{bookingId}?reason=changed%20my%20mind");
    }

    [Fact]
    public void CreateBooking_KeepsOnlyTheFirstProposalOfATurn()
    {
        var tools = TestFactory.Tools(_capture, new StubHandler(), Guid.NewGuid());
        var first = Guid.NewGuid();

        tools.CreateBooking(first, "2026-07-10", "2026-07-12");
        tools.CreateBooking(Guid.NewGuid(), "2026-08-10", "2026-08-12");

        using var doc = JsonDocument.Parse(_capture.Pending!.JsonBody!);
        doc.RootElement.GetProperty("catalogId").GetGuid().Should().Be(first);
    }

    // --- Reads hit the gateway and run un-gated ---

    [Fact]
    public async Task SearchCatalogs_SendsPaginationParams_AndReturnsBody()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """[{"id":"x"}]""");
        var tools = TestFactory.Tools(_capture, handler, Guid.NewGuid());

        var result = await tools.SearchCatalogs("beach house");

        result.Should().Be("""[{"id":"x"}]""");
        _capture.Pending.Should().BeNull(); // reads never propose
        var query = handler.LastRequest!.RequestUri!.PathAndQuery;
        query.Should().StartWith("/api/search/catalogs?query=beach%20house");
        query.Should().Contain("page=1");
        query.Should().Contain("pageSize=20");
    }

    [Fact]
    public async Task SearchCatalogsByName_HitsCatalogSearch_ReturnsBody()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """[{"id":"x","title":"Seaside Villa"}]""");
        var tools = TestFactory.Tools(_capture, handler, Guid.NewGuid());

        var result = await tools.SearchCatalogsByName("Seaside Villa");

        result.Should().Contain("Seaside Villa");
        _capture.Pending.Should().BeNull(); // reads never propose
        handler.LastRequest!.RequestUri!.PathAndQuery
            .Should().Be("/api/catalog/catalogs/search?name=Seaside%20Villa");
    }

    [Fact]
    public async Task GetCatalog_NotFound_ReturnsFriendlyMessage()
    {
        var handler = new StubHandler(HttpStatusCode.NotFound);
        var catalogId = Guid.NewGuid();
        var tools = TestFactory.Tools(_capture, handler, Guid.NewGuid());

        var result = await tools.GetCatalog(catalogId);

        result.Should().Be("Catalog not found.");
        handler.LastRequest!.RequestUri!.PathAndQuery.Should().Be($"/api/catalog/catalogs/{catalogId}");
    }

    [Fact]
    public async Task GetBooking_NotFound_ReturnsFriendlyMessage()
    {
        var handler = new StubHandler(HttpStatusCode.NotFound);
        var bookingId = Guid.NewGuid();
        var tools = TestFactory.Tools(_capture, handler, Guid.NewGuid());

        var result = await tools.GetBooking(bookingId);

        result.Should().Be("Booking not found.");
        handler.LastRequest!.RequestUri!.PathAndQuery.Should().Be($"/api/bookings/{bookingId}");
    }
}
