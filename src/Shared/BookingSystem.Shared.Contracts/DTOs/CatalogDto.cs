namespace BookingSystem.Shared.Contracts.DTOs;

public record CatalogDto(
    Guid Id,
    string Title,
    string Description,
    decimal PricePerNight,
    string Currency,
    bool IsAvailable);

public static class CatalogDtoExtensions
{
    public static bool IsAvailable(this CatalogDto catalog, DateRange period) =>
        catalog.IsAvailable;

    public static (decimal Amount, string Currency) CalculatePrice(this CatalogDto catalog, DateRange period) =>
        (catalog.PricePerNight * period.Nights, catalog.Currency);
}

public record DateRange(DateOnly CheckIn, DateOnly CheckOut)
{
    public int Nights => CheckOut.DayNumber - CheckIn.DayNumber;
}
