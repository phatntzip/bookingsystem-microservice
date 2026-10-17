using BookingSystem.BookingService.Application.Exceptions;
using BookingSystem.BookingService.Domain.ValueObjects;
using BookingSystem.Shared.Messaging;
using FluentAssertions;

namespace BookingService.Application.Tests.Exceptions;

public class NotFoundExceptionTests
{
    [Fact]
    public void Constructor_PreservesMessage()
    {
        new NotFoundException("Booking abc not found.").Message
            .Should().Be("Booking abc not found.");
    }

    [Fact]
    public void IsPermanentMessageException_SoKafkaConsumersDoNotRetry()
    {
        new NotFoundException("gone").Should().BeAssignableTo<IPermanentMessageException>();
    }
}

public class BookingOverlapExceptionTests
{
    [Fact]
    public void Constructor_HasDescriptiveDefaultMessage()
    {
        new BookingOverlapException().Message
            .Should().Be("A booking already exists for the requested catalog and dates.");
    }
}

public class CatalogNotAvailableExceptionTests
{
    [Fact]
    public void Constructor_MessageContainsCatalogIdAndPeriod()
    {
        var catalogId = Guid.NewGuid();
        var period = new DateRange(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 5));

        var exception = new CatalogNotAvailableException(catalogId, period);

        exception.Message.Should().Contain(catalogId.ToString());
        // DateOnly.ToString() is culture-dependent, so compare against the same formatting
        exception.Message.Should().Contain(period.CheckIn.ToString()).And.Contain(period.CheckOut.ToString());
    }
}
