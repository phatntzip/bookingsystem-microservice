using BookingSystem.CatalogService.Api.Features.GetCatalog;
using BookingSystem.CatalogService.Infrastructure.Persistence;
using BookingSystem.CatalogService.Infrastructure.Repositories;
using BookingSystem.Shared.Contracts.DTOs;
using FluentAssertions;
using NSubstitute;

namespace CatalogService.Tests.Features;

public class GetCatalogByIdHandlerTests
{
    private readonly ICatalogRepository _repo = Substitute.For<ICatalogRepository>();

    [Fact]
    public async Task Handle_CatalogNotFound_ReturnsNull()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Catalog?)null);

        var dto = await new GetCatalogByIdHandler(_repo).Handle(new GetCatalogByIdQuery(Guid.NewGuid()), default);

        dto.Should().BeNull();
    }

    [Fact]
    public async Task Handle_CatalogExists_MapsAllFieldsToSharedDto()
    {
        var catalog = new Catalog
        {
            Id = Guid.NewGuid(),
            Title = "Beach House",
            Description = "A house on the beach",
            PricePerNight = 150m,
            Currency = "USD",
            IsAvailable = false,
            CreatedAt = DateTime.UtcNow
        };
        _repo.GetByIdAsync(catalog.Id, Arg.Any<CancellationToken>()).Returns(catalog);

        var dto = await new GetCatalogByIdHandler(_repo).Handle(new GetCatalogByIdQuery(catalog.Id), default);

        dto.Should().Be(new CatalogDto(catalog.Id, "Beach House", "A house on the beach", 150m, "USD", false));
    }
}
