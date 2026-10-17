using BookingSystem.BookingService.Domain.ValueObjects;

namespace BookingSystem.BookingService.Domain.Repositories;

public interface IBookingRepository
{
    Task<Booking?> GetByIdAsync(BookingId id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Booking>> GetAllAsync(CancellationToken cancellationToken = default);
    Task AddAsync(Booking booking, CancellationToken cancellationToken = default);
    Task<bool> HasOverlapAsync(CatalogId catalogId, DateRange period, CancellationToken cancellationToken = default);
}
