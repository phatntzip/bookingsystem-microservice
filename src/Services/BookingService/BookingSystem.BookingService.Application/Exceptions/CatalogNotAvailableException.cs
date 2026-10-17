using BookingSystem.BookingService.Domain.ValueObjects;

namespace BookingSystem.BookingService.Application.Exceptions;

public class CatalogNotAvailableException(Guid catalogId, DateRange period)
    : Exception($"Catalog {catalogId} is not available from {period.CheckIn} to {period.CheckOut}.");
