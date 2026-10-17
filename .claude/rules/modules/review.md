# Review Module — ReviewService

## Location
`src/Services/ReviewService/`

## Structure (Pattern B — 2-layer)
```
BookingSystem.ReviewService.Api/
├── Program.cs
├── Endpoints/ReviewEndpoints.cs
└── Features/CreateReview/
    ├── CreateReviewCommand.cs
    └── CreateReviewHandler.cs   ← command + handler in same file

BookingSystem.ReviewService.Infrastructure/
└── Persistence/
    ├── ReviewDbContext.cs        ← Review entity + IReviewRepository + ReviewRepository
    ├── ReviewDbContextFactory.cs ← IDesignTimeDbContextFactory for dotnet ef
    └── Migrations/
```

## Entity

Defined in `ReviewDbContext.cs`:

```csharp
public class Review
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public Guid CatalogId { get; set; }
    public Guid UserId { get; set; }
    public int Rating { get; set; }                          // validated 1–5 in handler
    public string Comment { get; set; } = string.Empty;     // max 2000
    public DateTime CreatedAt { get; set; }
}
```

Table name: `reviews`; columns mapped to `snake_case` (`booking_id`, `catalog_id`, `user_id`, `created_at`). Configuration inline in `OnModelCreating`.

## Repository

`IReviewRepository` is defined in the same file as `ReviewDbContext`:

```csharp
public interface IReviewRepository
{
    Task AddAsync(Review review, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Review>> GetByCatalogAsync(Guid catalogId, CancellationToken cancellationToken = default);
}
```

`ReviewRepository.AddAsync` calls `SaveChangesAsync` directly — no `UnitOfWork`.  
`GetByCatalogAsync` orders by `CreatedAt` descending.

## Command

```csharp
public record CreateReviewCommand(
    Guid BookingId, Guid CatalogId, Guid UserId,
    int Rating, string Comment) : IRequest<Guid>;
```

`CreateReviewHandler` validates `Rating` is between 1 and 5 (throws `ArgumentOutOfRangeException` otherwise) before persisting.

## Endpoints

| Method | Path | Handler | Auth (gateway) |
|---|---|---|---|
| POST | `/api/reviews` | `CreateReviewCommand` | Required (`.RequireAuthorization()` on route) |
| GET | `/api/reviews/catalog/{catalogId:guid}` | Direct `IReviewRepository` call | None |

`GET` does not use MediatR — the endpoint injects `IReviewRepository` directly.

## DI registration (Program.cs)

```csharp
builder.AddNpgsqlDbContext<ReviewDbContext>("reviewdb");
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));
builder.Services.AddScoped<IReviewRepository, ReviewRepository>();
```

No Redis, no Kafka. Has a `RunMigrationsOnStartup` block using `MigrateWithRetryAsync` (set to `true` in `appsettings.json`).

## Kafka

ReviewService has **no Kafka producer or consumer**. Reviews are submitted via direct HTTP and are not propagated to any other service.

## Gaps

- No aggregate rating endpoint — see GitHub issue #17
- No validation that a review's `BookingId` belongs to the `UserId` (any user can post any review)
- No update or delete endpoint
- `review.rating.updated` Kafka event (issue #17) not yet published
