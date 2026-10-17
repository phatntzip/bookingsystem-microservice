using BookingSystem.CatalogService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BookingSystem.CatalogService.Infrastructure.Repositories;

public class CatalogRepository(CatalogDbContext db) : ICatalogRepository
{
    public Task<Catalog?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => db.Catalogs.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Catalog>> GetAllAsync(CancellationToken cancellationToken = default)
        => await db.Catalogs.OrderByDescending(l => l.CreatedAt).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Catalog>> SearchByNameAsync(string name, CancellationToken cancellationToken = default)
        => await db.Catalogs
            .Where(c => EF.Functions.ILike(c.Title, $"%{name}%"))   // case-insensitive substring (Postgres)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(Catalog catalog, CancellationToken cancellationToken = default)
    {
        await db.Catalogs.AddAsync(catalog, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }
}
