using BookingSystem.CatalogService.Infrastructure.Persistence;
using BookingSystem.CatalogService.Infrastructure.Repositories;
using MediatR;

namespace BookingSystem.CatalogService.Api.Features.CreateCatalog;

public record CreateCatalogCommand(
    string Title,
    string Description,
    decimal PricePerNight,
    string Currency) : IRequest<Guid>;

public class CreateCatalogHandler(ICatalogRepository repo)
    : IRequestHandler<CreateCatalogCommand, Guid>
{
    public async Task<Guid> Handle(CreateCatalogCommand cmd, CancellationToken cancellationToken)
    {
        var catalog = new Catalog
        {
            Id = Guid.NewGuid(),
            Title = cmd.Title,
            Description = cmd.Description,
            PricePerNight = cmd.PricePerNight,
            Currency = cmd.Currency,
            IsAvailable = true,
            CreatedAt = DateTime.UtcNow
        };
        await repo.AddAsync(catalog, cancellationToken);
        return catalog.Id;
    }
}
