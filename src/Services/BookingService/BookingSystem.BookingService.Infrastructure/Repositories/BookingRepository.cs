using BookingSystem.BookingService.Domain;
using BookingSystem.BookingService.Domain.Repositories;
using BookingSystem.BookingService.Domain.ValueObjects;
using BookingSystem.BookingService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BookingSystem.BookingService.Infrastructure.Repositories;

public class BookingRepository(BookingDbContext dbContext) : IBookingRepository
{
    public Task<Booking?> GetByIdAsync(BookingId id, CancellationToken cancellationToken = default)
        => dbContext.Bookings.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Booking>> GetAllAsync(CancellationToken cancellationToken = default)
        => await dbContext.Bookings.OrderByDescending(b => b.Period.CheckIn).ToListAsync(cancellationToken);

    public async Task AddAsync(Booking booking, CancellationToken cancellationToken = default)
        => await dbContext.Bookings.AddAsync(booking, cancellationToken);

    public Task<bool> HasOverlapAsync(CatalogId catalogId, DateRange period, CancellationToken cancellationToken = default)
        => dbContext.Bookings.AnyAsync(b =>
            b.CatalogId == catalogId &&
            b.Status != BookingStatus.Cancelled &&
            b.Period.CheckIn < period.CheckOut &&
            b.Period.CheckOut > period.CheckIn, cancellationToken);
}
