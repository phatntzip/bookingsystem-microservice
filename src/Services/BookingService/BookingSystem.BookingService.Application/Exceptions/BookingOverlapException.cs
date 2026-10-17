namespace BookingSystem.BookingService.Application.Exceptions;

public class BookingOverlapException()
    : Exception("A booking already exists for the requested catalog and dates.");
