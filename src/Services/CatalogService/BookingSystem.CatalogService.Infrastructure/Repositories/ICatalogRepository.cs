using BookingSystem.CatalogService.Infrastructure.Persistence;

namespace BookingSystem.CatalogService.Infrastructure.Repositories;

public interface ICatalogRepository
{
    Task<Catalog?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Catalog>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Catalog>> SearchByNameAsync(string name, CancellationToken cancellationToken = default);
    Task AddAsync(Catalog catalog, CancellationToken cancellationToken = default);
}
