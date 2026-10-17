using BookingSystem.BookingService.Application.Commands.CancelBooking;
using BookingSystem.BookingService.Application.Commands.CreateBooking;
using BookingSystem.BookingService.Application.Queries.GetBooking;
using BookingSystem.BookingService.Application.Queries.GetBookings;
using MediatR;

namespace BookingSystem.BookingService.Api.Endpoints;

public static class BookingEndpoints
{
    public static void MapBookingEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/bookings");

        group.MapGet("/", async (ISender sender) =>
        {
            var bookings = await sender.Send(new GetBookingsQuery());
            return Results.Ok(bookings);
        })
        .WithName("ListBookings");

        group.MapPost("/", async (CreateBookingCommand cmd, ISender sender) =>
        {
            var id = await sender.Send(cmd);
            return Results.Created($"/api/bookings/{id.Value}", new { id = id.Value });
        })
        .WithName("CreateBooking");

        group.MapGet("/{id:guid}", async (Guid id, ISender sender) =>
        {
            var dto = await sender.Send(new GetBookingQuery(id));
            return Results.Ok(dto);
        })
        .WithName("GetBooking");

        group.MapDelete("/{id:guid}", async (Guid id, string reason, ISender sender) =>
        {
            await sender.Send(new CancelBookingCommand(id, reason));
            return Results.NoContent();
        })
        .WithName("CancelBooking");
    }
}
