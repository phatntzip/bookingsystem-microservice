using BookingSystem.BookingService.Application.Commands.CreateBooking;
using BookingSystem.BookingService.Application.Exceptions;
using BookingSystem.BookingService.Application.Interfaces;
using BookingSystem.BookingService.Application.Interfaces.UoW;
using BookingSystem.BookingService.Domain;
using BookingSystem.BookingService.Domain.Repositories;
using BookingSystem.BookingService.Domain.ValueObjects;
using BookingSystem.Shared.Contracts.DTOs;
using FluentAssertions;
using NSubstitute;
// CatalogDto's namespace also declares a DateRange record — pin to the domain one
using DateRange = BookingSystem.BookingService.Domain.ValueObjects.DateRange;

namespace BookingService.Application.Tests.Commands;

public class CreateBookingHandlerTests
{
    private readonly IBookingRepository _bookingRepo = Substitute.For<IBookingRepository>();
    private readonly ICatalogServiceClient _catalogClient = Substitute.For<ICatalogServiceClient>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private CreateBookingHandler CreateHandler() =>
        new(_bookingRepo, _catalogClient, _unitOfWork);

    private static CreateBookingCommand CreateCommand(Guid? catalogId = null) =>
        new(Guid.NewGuid(), catalogId ?? Guid.NewGuid(),
            new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 5));

    private static CatalogDto CreateCatalog(Guid id, bool isAvailable = true, decimal pricePerNight = 100m) =>
        new(id, "Beach House", "A house on the beach", pricePerNight, "USD", isAvailable);

    [Fact]
    public async Task Handle_CatalogNotFound_ThrowsNotFoundException()
    {
        var cmd = CreateCommand();
        _catalogClient.GetCatalogAsync(cmd.CatalogId, Arg.Any<CancellationToken>())
            .Returns((CatalogDto?)null);

        var act = () => CreateHandler().Handle(cmd, default);

        await act.Should().ThrowAsync<NotFoundException>();
        await _bookingRepo.DidNotReceive().AddAsync(Arg.Any<Booking>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CatalogNotAvailable_ThrowsCatalogNotAvailableException()
    {
        var cmd = CreateCommand();
        _catalogClient.GetCatalogAsync(cmd.CatalogId, Arg.Any<CancellationToken>())
            .Returns(CreateCatalog(cmd.CatalogId, isAvailable: false));

        var act = () => CreateHandler().Handle(cmd, default);

        await act.Should().ThrowAsync<CatalogNotAvailableException>();
        await _bookingRepo.DidNotReceive().AddAsync(Arg.Any<Booking>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_OverlappingBookingExists_ThrowsBookingOverlapException()
    {
        var cmd = CreateCommand();
        _catalogClient.GetCatalogAsync(cmd.CatalogId, Arg.Any<CancellationToken>())
            .Returns(CreateCatalog(cmd.CatalogId));
        _bookingRepo.HasOverlapAsync(new CatalogId(cmd.CatalogId), Arg.Any<DateRange>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var act = () => CreateHandler().Handle(cmd, default);

        await act.Should().ThrowAsync<BookingOverlapException>();
        await _bookingRepo.DidNotReceive().AddAsync(Arg.Any<Booking>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ValidCommand_AddsBookingAndCommits()
    {
        var cmd = CreateCommand();
        _catalogClient.GetCatalogAsync(cmd.CatalogId, Arg.Any<CancellationToken>())
            .Returns(CreateCatalog(cmd.CatalogId));

        var result = await CreateHandler().Handle(cmd, default);

        result.Should().NotBeNull();
        result.Value.Should().NotBeEmpty();
        await _bookingRepo.Received(1).AddAsync(Arg.Any<Booking>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ValidCommand_TotalPriceIsPricePerNightTimesNights()
    {
        var cmd = CreateCommand(); // 4 nights
        _catalogClient.GetCatalogAsync(cmd.CatalogId, Arg.Any<CancellationToken>())
            .Returns(CreateCatalog(cmd.CatalogId, pricePerNight: 150m));

        Booking? added = null;
        await _bookingRepo.AddAsync(Arg.Do<Booking>(b => added = b), Arg.Any<CancellationToken>());

        await CreateHandler().Handle(cmd, default);

        added.Should().NotBeNull();
        added!.TotalPrice.Should().Be(new Money(600m, "USD"));
        added.Period.Nights.Should().Be(4);
        added.Status.Should().Be(BookingStatus.Pending);
    }
}
