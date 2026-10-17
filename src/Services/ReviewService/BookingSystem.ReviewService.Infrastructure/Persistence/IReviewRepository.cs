namespace BookingSystem.ReviewService.Infrastructure.Persistence;

public interface IReviewRepository
{
    Task AddAsync(Review review, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Review>> GetByCatalogAsync(Guid catalogId, CancellationToken cancellationToken = default);
}
