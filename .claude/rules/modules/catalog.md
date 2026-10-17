# Catalog Module — CatalogService

## Location
`src/Services/CatalogService/`

## Structure (Pattern B — 2-layer)
```
BookingSystem.CatalogService.Api/
├── Program.cs
├── Endpoints/CatalogEndpoints.cs
└── Features/
    ├── CreateCatalog/
    │   ├── CreateCatalogCommand.cs
    │   └── CreateCatalogHandler.cs
    └── GetCatalog/
        ├── GetCatalogByIdQuery.cs
        └── GetCatalogByIdHandler.cs

BookingSystem.CatalogService.Infrastructure/
└── Persistence/
    ├── CatalogDbContext.cs          ← Catalog entity defined here
    ├── CatalogDbContextFactory.cs
    ├── Migrations/
    └── Repositories/
        ├── ICatalogRepository.cs    ← note: named ICatalogRepository
        └── CatalogRepository.cs
```

## Entity

Defined in `CatalogDbContext.cs`:

```csharp
public class Catalog
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;       // max 300, required
    public string Description { get; set; } = string.Empty;
    public decimal PricePerNight { get; set; }               // decimal(18,2)
    public string Currency { get; set; } = "USD";            // max 3
    public bool IsAvailable { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}
```

Table name: `catalogs`. Configuration is inline in `OnModelCreating`.

## Repository

The repository interface is named `ICatalogRepository`, not `ICatalogRepository`:

```csharp
public interface ICatalogRepository
{
    Task<Catalog?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(Catalog catalog, CancellationToken cancellationToken = default);
}
```

`CatalogRepository.AddAsync` calls `SaveChangesAsync` directly — no `UnitOfWork`.

## Shared DTO (Shared.Contracts)

`CatalogDto` lives in `BookingSystem.Shared.Contracts/DTOs/CatalogDto.cs` and is used by both CatalogService and BookingService:

```csharp
public record CatalogDto(
    Guid Id, string Title, string Description,
    decimal PricePerNight, string Currency, bool IsAvailable);
```

`GetCatalogByIdHandler` maps `Catalog` → `CatalogDto`. `CreateCatalogHandler` returns the raw `Guid`.

## Commands and queries

```csharp
public record CreateCatalogCommand(string Title, string Description, decimal PricePerNight, string Currency) : IRequest<Guid>;
public record GetCatalogByIdQuery(Guid CatalogId) : IRequest<CatalogDto?>;
```

## Endpoints

| Method | Path | Handler | Auth (gateway) |
|---|---|---|---|
| GET | `/api/catalog/catalogs/{id:guid}` | `GetCatalogByIdQuery` | None |
| POST | `/api/catalog/catalogs` | `CreateCatalogCommand` | None |

Note the double segment: the group is `/api/catalog` and the routes add `/catalogs`, so the full path is `/api/catalog/catalogs/...`. `BookingService`'s `CatalogServiceClient` calls `/api/catalog/catalogs/{catalogId}`.

## DI registration (Program.cs)

```csharp
builder.AddNpgsqlDbContext<CatalogDbContext>("catalogdb");
builder.AddRedisDistributedCache("redis");
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));
builder.Services.AddScoped<ICatalogRepository, CatalogRepository>();
```

Has `RunMigrationsOnStartup` block using `MigrateWithRetryAsync`.

## Kafka

CatalogService has **no Kafka producer or consumer** in the current implementation. The `catalog.availability.updated` topic mentioned in architecture docs is not yet wired — see GitHub issue #16.

## Gaps

- No Kafka output (availability events not published)
- No availability date blocking/unblocking — see GitHub issue #16
- No `IsAvailable` update endpoint (flag can only be set at creation time)
- Redis cache (`catalog:{id}`) is registered but the repository does not read/write it
