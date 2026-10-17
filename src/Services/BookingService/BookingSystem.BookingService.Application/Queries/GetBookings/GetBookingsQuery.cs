using BookingSystem.BookingService.Application.DTOs;
using MediatR;

namespace BookingSystem.BookingService.Application.Queries.GetBookings;

public record GetBookingsQuery : IRequest<IReadOnlyList<BookingDto>>;
