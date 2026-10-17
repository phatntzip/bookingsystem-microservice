using BookingSystem.CatalogService.Infrastructure.Repositories;
using BookingSystem.Shared.Contracts.DTOs;
using MediatR;

namespace BookingSystem.CatalogService.Api.Features.GetCatalog;

public record GetCatalogByIdQuery(Guid CatalogId) : IRequest<CatalogDto?>;

public class GetCatalogByIdHandler(ICatalogRepository repo)
    : IRequestHandler<GetCatalogByIdQuery, CatalogDto?>
{
    public async Task<CatalogDto?> Handle(GetCatalogByIdQuery query, CancellationToken cancellationToken)
    {
        var catalog = await repo.GetByIdAsync(query.CatalogId, cancellationToken);
        return catalog is null ? null
            : new CatalogDto(
                catalog.Id,
                catalog.Title,
                catalog.Description,
                catalog.PricePerNight,
                catalog.Currency,
                catalog.IsAvailable);
    }
}
