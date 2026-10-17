using BookingSystem.BookingService.Application.DTOs;
using BookingSystem.BookingService.Domain.Repositories;
using MediatR;

namespace BookingSystem.BookingService.Application.Queries.GetBookings;

public class GetBookingsHandler(IBookingRepository repo)
    : IRequestHandler<GetBookingsQuery, IReadOnlyList<BookingDto>>
{
    public async Task<IReadOnlyList<BookingDto>> Handle(GetBookingsQuery q, CancellationToken cancellationToken)
    {
        var bookings = await repo.GetAllAsync(cancellationToken);

        return bookings.Select(b => new BookingDto(
            b.Id.Value,
            b.UserId.Value,
            b.CatalogId.Value,
            b.Period.CheckIn,
            b.Period.CheckOut,
            b.TotalPrice.Amount,
            b.TotalPrice.Currency,
            b.Status.ToString())).ToList();
    }
}
