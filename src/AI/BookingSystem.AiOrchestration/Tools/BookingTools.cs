using System.ComponentModel;
using System.Net;
using System.Text.Json;
using BookingSystem.AiOrchestration.Chat;

namespace BookingSystem.AiOrchestration.Tools;

/// <summary>
/// The tools the model may call. Reads hit the gateway directly (auto-invoked). Writes do NOT
/// mutate — they record a proposal via <see cref="ProposalCapture"/> for the user to confirm.
/// </summary>
public sealed class BookingTools(
    IHttpClientFactory httpClientFactory,
    ProposalCapture capture,
    IConfiguration config,
    ILogger<BookingTools> logger)
{
    private HttpClient Gateway => httpClientFactory.CreateClient("gateway");

    [Description("Search bookable catalogs by free-text query (e.g. a place or title). Read-only; runs immediately.")]
    public async Task<string> SearchCatalogs(
        [Description("Free-text search terms, e.g. 'beach house in Da Nang'.")] string query,
        [Description("1-based page number. Default 1.")] int page = 1,
        [Description("Results per page. Default 20.")] int pageSize = 20)
    {
        var resp = await Gateway.GetAsync(
            $"/api/search/catalogs?query={Uri.EscapeDataString(query)}&page={page}&pageSize={pageSize}");
        var body = await resp.Content.ReadAsStringAsync();
        return resp.IsSuccessStatusCode ? body : $"Search failed ({(int)resp.StatusCode}). {body}";
    }

    [Description("Find bookable catalogs by name/title (matches the catalog directly, case-insensitive). Use this to locate a catalog and its GUID when the user names it instead of giving an id. Read-only; runs immediately.")]
    public async Task<string> SearchCatalogsByName(
        [Description("The catalog name, or part of it, e.g. 'Seaside Villa'.")] string name)
    {
        var resp = await Gateway.GetAsync($"/api/catalog/catalogs/search?name={Uri.EscapeDataString(name)}");
        var body = await resp.Content.ReadAsStringAsync();
        return resp.IsSuccessStatusCode ? body : $"Search failed ({(int)resp.StatusCode}). {body}";
    }

    [Description("Get one catalog by its GUID id, including price per night and availability. Read-only.")]
    public async Task<string> GetCatalog(
        [Description("The catalog GUID.")] Guid catalogId)
    {
        var resp = await Gateway.GetAsync($"/api/catalog/catalogs/{catalogId}");
        if (resp.StatusCode == HttpStatusCode.NotFound) return "Catalog not found.";
        var body = await resp.Content.ReadAsStringAsync();
        return resp.IsSuccessStatusCode ? body : $"Lookup failed ({(int)resp.StatusCode}). {body}";
    }

    [Description("Get an existing booking by its GUID id. Read-only.")]
    public async Task<string> GetBooking(
        [Description("The booking GUID.")] Guid bookingId)
    {
        var resp = await Gateway.GetAsync($"/api/bookings/{bookingId}");
        if (resp.StatusCode == HttpStatusCode.NotFound) return "Booking not found.";
        var body = await resp.Content.ReadAsStringAsync();
        return resp.IsSuccessStatusCode ? body : $"Lookup failed ({(int)resp.StatusCode}). {body}";
    }

    [Description("Prepare a NEW booking for the current user. Write action: does not book immediately — it records a proposal the user must confirm first.")]
    public string CreateBooking(
        [Description("The catalog GUID to book.")] Guid catalogId,
        [Description("Check-in date, ISO format yyyy-MM-dd.")] string checkIn,
        [Description("Check-out date, ISO format yyyy-MM-dd.")] string checkOut)
    {
        if (!DateOnly.TryParse(checkIn, out var ci) || !DateOnly.TryParse(checkOut, out var co))
            return "Invalid date. Use ISO format yyyy-MM-dd for both checkIn and checkOut.";
        if (co <= ci)
            return "Check-out must be after check-in.";

        var userId = config.GetValue<Guid?>("Booking:DemoUserId") ?? Guid.Empty;
        var payload = new
        {
            userId,
            catalogId,
            checkIn = ci.ToString("yyyy-MM-dd"),
            checkOut = co.ToString("yyyy-MM-dd"),
        };

        var nights = co.DayNumber - ci.DayNumber;
        var summary = $"Create booking for catalog {catalogId} from {ci:yyyy-MM-dd} to {co:yyyy-MM-dd} ({nights} night(s)).";
        logger.LogInformation("Proposing CreateBooking: {Summary}", summary);
        capture.Propose(new PendingAction("CreateBooking", summary, HttpMethod.Post, "/api/bookings", JsonSerializer.Serialize(payload)));
        return $"Prepared: {summary} Awaiting the user's confirmation before it is booked.";
    }

    [Description("Prepare cancellation of an existing booking. Write action: records a proposal the user must confirm first.")]
    public string CancelBooking(
        [Description("The booking GUID to cancel.")] Guid bookingId,
        [Description("Reason for cancellation.")] string reason)
    {
        var path = $"/api/bookings/{bookingId}?reason={Uri.EscapeDataString(reason)}";
        var summary = $"Cancel booking {bookingId} (reason: {reason}).";
        logger.LogInformation("Proposing CancelBooking: {Summary}", summary);
        capture.Propose(new PendingAction("CancelBooking", summary, HttpMethod.Delete, path, null));
        return $"Prepared: {summary} Awaiting the user's confirmation before it is cancelled.";
    }
}
