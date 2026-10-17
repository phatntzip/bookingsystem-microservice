# Booking Module — BookingService

## Location
`src/Services/BookingService/` — the only service using Pattern A (4-layer Clean Architecture).

## Layer structure

```
BookingSystem.BookingService.Domain/
├── Aggregates/Booking.cs
├── Common/AggregateRoot.cs, IDomainEvent.cs
├── Events/BookingCreatedEvent.cs, BookingConfirmedEvent.cs, BookingCancelledEvent.cs
├── Exceptions/BookingDomainException.cs
├── Repositories/IBookingRepository.cs
└── ValueObjects/BookingId.cs, UserId.cs, CatalogId.cs, DateRange.cs, Money.cs, BookingStatus.cs

BookingSystem.BookingService.Application/
├── Commands/
│   ├── CreateBooking/CreateBookingCommand.cs + CreateBookingHandler.cs
│   ├── CancelBooking/CancelBookingCommand.cs + CancelBookingHandler.cs
│   └── ConfirmBooking/ConfirmBookingCommand.cs + ConfirmBookingHandler.cs
├── Queries/GetBooking/GetBookingQuery.cs + GetBookingHandler.cs
├── DTOs/BookingDto.cs
├── EventHandlers/BookingEventHandlers.cs
├── Exceptions/NotFoundException.cs, BookingOverlapException.cs, CatalogNotAvailableException.cs
└── Interfaces/ICatalogServiceClient.cs, IUserServiceClient.cs, UoW/IUnitOfWork.cs

BookingSystem.BookingService.Infrastructure/
├── HttpClients/CatalogServiceClient.cs, UserServiceClient.cs
├── Messaging/UnitOfWork.cs
├── Outbox/OutboxMessage.cs, OutboxProcessor.cs
├── Persistence/BookingDbContext.cs, BookingDbContextFactory.cs
├── Persistence/Configurations/BookingConfiguration.cs, OutboxMessageConfiguration.cs
├── Persistence/Migrations/
└── Repositories/BookingRepository.cs

BookingSystem.BookingService.Api/
├── Program.cs
└── Endpoints/BookingEndpoints.cs
```

## Aggregate

```csharp
public class Booking : AggregateRoot<BookingId>
{
    public UserId UserId { get; private set; }
    public CatalogId CatalogId { get; private set; }
    public DateRange Period { get; private set; }
    public Money TotalPrice { get; private set; }
    public BookingStatus Status { get; private set; }  // enum: Pending, Confirmed, Cancelled, Completed
}
```

State transitions and their guards:

| Method | Guard | Domain event raised |
|---|---|---|
| `Booking.Create(...)` | — | `BookingCreatedEvent` |
| `booking.Confirm()` | Status must be `Pending` | `BookingConfirmedEvent` |
| `booking.Cancel(reason)` | Status must not be `Cancelled` | `BookingCancelledEvent` |
| `booking.Complete()` | Status must be `Confirmed` | _(none)_ |

All guards throw `BookingDomainException` on violation.

## Value objects

| Type | EF mapping |
|---|---|
| `BookingId(Guid Value)` | `HasConversion(id => id.Value, v => new BookingId(v))` |
| `UserId(Guid Value)` | Same pattern |
| `CatalogId(Guid Value)` | Same pattern |
| `DateRange(CheckIn, CheckOut)` | `OwnsOne` → `check_in`, `check_out` columns + indexes |
| `Money(Amount, Currency)` | `OwnsOne` → `price_amount decimal(18,2)`, `price_currency` columns |
| `BookingStatus` | `HasConversion<string>().HasMaxLength(20)` |

`BookingId` has `static New()`. `CatalogId` and `UserId` do not.

## Domain events → Kafka

`BookingEventHandlers.cs` contains `INotificationHandler` implementations that translate domain events to integration events and publish to Kafka:

| Domain event | Kafka topic | Integration event |
|---|---|---|
| `BookingCreatedEvent` | `booking.created` | `BookingCreatedIntegrationEvent` |
| `BookingCancelledEvent` | `booking.cancelled` | `BookingCancelledIntegrationEvent` |

`BookingConfirmedEvent` has no Kafka handler — it is internal only.

## Outbox pattern

`UnitOfWork.CommitAsync`:
1. Enumerates `AggregateRoot` entries from `ChangeTracker`, collects `DomainEvents`
2. Serializes each to `OutboxMessage` (EventType = assembly-qualified name, Payload = JSON)
3. Clears `DomainEvents` on each aggregate
4. Calls `SaveChangesAsync` — entity change + outbox rows commit atomically

`OutboxProcessor` (BackgroundService, polls every 5 s, batch 20):
1. Queries `outbox_messages WHERE ProcessedAt IS NULL AND Error IS NULL ORDER BY CreatedAt`
2. Deserializes via `Type.GetType(EventType)`, publishes via MediatR `IPublisher`
3. Marks `ProcessedAt` on success, `Error` on failure
4. One `SaveChangesAsync` per batch

## Commands and queries

```csharp
public record CreateBookingCommand(Guid UserId, Guid CatalogId, DateOnly CheckIn, DateOnly CheckOut) : IRequest<BookingId>;
public record CancelBookingCommand(Guid BookingId, string Reason) : IRequest;
public record ConfirmBookingCommand(Guid BookingId) : IRequest;
public record GetBookingQuery(Guid BookingId) : IRequest<BookingDto>;
```

`CreateBookingHandler` flow:
1. Build `DateRange` (validates `CheckOut > CheckIn`)
2. `ICatalogServiceClient.GetCatalogAsync` → throws `NotFoundException` if null
3. Check `catalog.IsAvailable` → throws `CatalogNotAvailableException` if false
4. `IBookingRepository.HasOverlapAsync` → throws `BookingOverlapException` if overlapping booking exists
5. Calculate `totalPrice = PricePerNight × Nights`
6. `Booking.Create(...)` → raises `BookingCreatedEvent`
7. `bookingRepo.AddAsync` + `unitOfWork.CommitAsync`

`ConfirmBookingCommand` is triggered by `payment.succeeded` (PaymentService Kafka event → future consumer, not yet wired in BookingService).

## HTTP endpoints

| Method | Path | Command/Query | Auth (gateway) |
|---|---|---|---|
| POST | `/api/bookings` | `CreateBookingCommand` | Required |
| GET | `/api/bookings/{id:guid}` | `GetBookingQuery` | Required |
| DELETE | `/api/bookings/{id:guid}?reason=` | `CancelBookingCommand` | Required |

`reason` on DELETE is a query string parameter, not request body. `ConfirmBooking` has no HTTP endpoint — it is command-only.

## Repository interface

```csharp
public interface IBookingRepository
{
    Task<Booking?> GetByIdAsync(BookingId id, CancellationToken ct = default);
    Task AddAsync(Booking booking, CancellationToken ct = default);
    Task<bool> HasOverlapAsync(CatalogId catalogId, DateRange period, CancellationToken ct = default);
}
```

`BookingRepository.AddAsync` does **not** call `SaveChangesAsync` — that is deferred to `UnitOfWork.CommitAsync`.

## External HTTP calls

`CatalogServiceClient` → `GET /api/catalog/catalogs/{catalogId}` → returns `CatalogDto?`  
`UserServiceClient` → `GET /api/users/{userId}` → returns `bool` (success status check)  

Both use Aspire service discovery (`http://catalog-service`, `http://user-service`) and get `StandardResilienceHandler` automatically.

## DI registration (Program.cs)

```csharp
builder.AddNpgsqlDbContext<BookingDbContext>("bookingdb");
builder.AddRedisDistributedCache("redis");
builder.Services.AddMediatR(cfg => {
    cfg.RegisterServicesFromAssembly(typeof(CreateBookingCommand).Assembly); // Application
    cfg.RegisterServicesFromAssembly(typeof(Program).Assembly);              // Api
});
builder.Services.AddScoped<IBookingRepository, BookingRepository>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddHostedService<OutboxProcessor>();
builder.Services.AddHttpClient<ICatalogServiceClient, CatalogServiceClient>(
    c => c.BaseAddress = new Uri("http://catalog-service"));
builder.Services.AddHttpClient<IUserServiceClient, UserServiceClient>(
    c => c.BaseAddress = new Uri("http://user-service"));
builder.Services.AddKafkaMessaging(builder.Configuration);
```

## Gaps

- `ConfirmBookingCommand` exists but is not triggered by a Kafka consumer — PaymentService publishes `payment.succeeded` but BookingService has no consumer for it
- Booking date amendment — see GitHub issue #18
