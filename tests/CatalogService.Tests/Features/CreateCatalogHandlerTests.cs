using BookingSystem.CatalogService.Api.Features.CreateCatalog;
using BookingSystem.CatalogService.Infrastructure.Persistence;
using BookingSystem.CatalogService.Infrastructure.Repositories;
using FluentAssertions;
using NSubstitute;

namespace CatalogService.Tests.Features;

public class CreateCatalogHandlerTests
{
    private readonly ICatalogRepository _repo = Substitute.For<ICatalogRepository>();

    [Fact]
    public async Task Handle_ValidCommand_PersistsCatalogWithCommandValues()
    {
        Catalog? added = null;
        await _repo.AddAsync(Arg.Do<Catalog>(c => added = c), Arg.Any<CancellationToken>());
        var cmd = new CreateCatalogCommand("Beach House", "A house on the beach", 150m, "USD");

        var result = await new CreateCatalogHandler(_repo).Handle(cmd, default);

        added.Should().NotBeNull();
        added!.Title.Should().Be("Beach House");
        added.Description.Should().Be("A house on the beach");
        added.PricePerNight.Should().Be(150m);
        added.Currency.Should().Be("USD");
        added.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        result.Should().Be(added.Id);
    }

    [Fact]
    public async Task Handle_NewCatalog_IsAvailableByDefault()
    {
        Catalog? added = null;
        await _repo.AddAsync(Arg.Do<Catalog>(c => added = c), Arg.Any<CancellationToken>());

        await new CreateCatalogHandler(_repo)
            .Handle(new CreateCatalogCommand("T", "D", 10m, "USD"), default);

        added!.IsAvailable.Should().BeTrue();
    }
}
