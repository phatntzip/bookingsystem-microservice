# Search Module — SearchService

## Location
`src/Services/SearchService/`

## Structure (Pattern B — 2-layer, Elasticsearch instead of Postgres)
```
BookingSystem.SearchService.Api/
├── Program.cs
├── Endpoints/SearchEndpoints.cs
└── Features/SearchCatalogs/
    ├── SearchCatalogsQuery.cs
    └── SearchCatalogsHandler.cs   ← handler inline in same file

BookingSystem.SearchService.Infrastructure/
└── Search/
    └── ElasticsearchService.cs    ← ISearchService + ElasticsearchService + types
```

No `Persistence/` directory — SearchService uses Elasticsearch, not Postgres. No database migrations.

## Elasticsearch index

Index name: `catalogs`

Document type:
```csharp
public record CatalogDocument(
    Guid Id, string Title, string Description,
    decimal PricePerNight, string Currency, bool IsAvailable);
```

## ISearchService interface

```csharp
public interface ISearchService
{
    Task<SearchResult> SearchCatalogsAsync(
        string? query, DateOnly? checkIn, DateOnly? checkOut,
        decimal? maxPrice, int page, int pageSize,
        CancellationToken cancellationToken = default);

    Task IndexCatalogAsync(CatalogDocument catalog, CancellationToken cancellationToken = default);
}
```

`IndexCatalogAsync` exists on the interface but is not called from any handler or consumer in the current codebase — the index is not populated automatically.

## Search behaviour

`ElasticsearchService.SearchCatalogsAsync`:
- If `query` is non-empty: `MultiMatch` on `title` and `description` fields
- If `query` is null/empty: `MatchAll`
- `checkIn`, `checkOut`, `maxPrice` are **accepted as parameters but not applied** to the query — the Elasticsearch query ignores them
- Pagination via `from = (page - 1) * pageSize` + `size = pageSize`

Returns `SearchResult(IReadOnlyList<CatalogDocument> Items, long Total, int Page, int PageSize)`.

## Query and endpoint

```csharp
public record SearchCatalogsQuery(
    string? Query, DateOnly? CheckIn, DateOnly? CheckOut,
    decimal? MaxPrice, int Page = 1, int PageSize = 20) : IRequest<SearchResult>;
```

| Method | Path | Query params | Auth (gateway) |
|---|---|---|---|
| GET | `/api/search/catalogs` | `query`, `checkIn`, `checkOut`, `maxPrice`, `page`, `pageSize` | None |

## DI registration (Program.cs)

```csharp
builder.AddElasticsearchClient("elasticsearch");    // Aspire Elasticsearch integration
builder.AddRedisDistributedCache("redis");
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));
builder.Services.AddSingleton<ISearchService, ElasticsearchService>();
```

No `AddNpgsqlDbContext`. No Kafka consumer registration.

## Kafka

SearchService has **no Kafka consumer** in the current implementation. The `catalog.availability.updated` topic is not consumed. The Elasticsearch index must be populated via direct `IndexCatalogAsync` calls or a future consumer.

## Gaps

- `checkIn`, `checkOut`, `maxPrice` filters are not applied in the Elasticsearch query — see GitHub issue #16
- No Kafka consumer for `catalog.availability.updated` — index is never populated automatically
- `IndexCatalogAsync` is not called from any code path
- Redis cache is registered but not used for search result caching in the current implementation
