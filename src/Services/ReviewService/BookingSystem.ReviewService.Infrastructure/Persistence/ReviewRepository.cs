using Microsoft.EntityFrameworkCore;

namespace BookingSystem.ReviewService.Infrastructure.Persistence;

public class ReviewRepository(ReviewDbContext db) : IReviewRepository
{
    public async Task AddAsync(Review review, CancellationToken cancellationToken = default)
    {
        await db.Reviews.AddAsync(review, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Review>> GetByCatalogAsync(Guid catalogId, CancellationToken cancellationToken = default)
        => await db.Reviews
            .Where(r => r.CatalogId == catalogId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);
}
