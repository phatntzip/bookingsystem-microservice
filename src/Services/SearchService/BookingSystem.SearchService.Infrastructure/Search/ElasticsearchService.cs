using Elastic.Clients.Elasticsearch;

namespace BookingSystem.SearchService.Infrastructure.Search;

public record CatalogDocument(
    Guid Id,
    string Title,
    string Description,
    decimal PricePerNight,
    string Currency,
    bool IsAvailable);

public record SearchResult(IReadOnlyList<CatalogDocument> Items, long Total, int Page, int PageSize);

public interface ISearchService
{
    Task<SearchResult> SearchCatalogsAsync(
        string? query,
        DateOnly? checkIn,
        DateOnly? checkOut,
        decimal? maxPrice,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task IndexCatalogAsync(CatalogDocument catalog, CancellationToken cancellationToken = default);
}

public class ElasticsearchService(ElasticsearchClient client) : ISearchService
{
    private const string IndexName = "catalogs";

    public async Task<SearchResult> SearchCatalogsAsync(
        string? query,
        DateOnly? checkIn,
        DateOnly? checkOut,
        decimal? maxPrice,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var from = (page - 1) * pageSize;

        var response = await client.SearchAsync<CatalogDocument>(s => s
            .Indices(IndexName)
            .From(from)
            .Size(pageSize)
            .Query(q =>
            {
                if (!string.IsNullOrWhiteSpace(query))
                    q.MultiMatch(m => m
                        .Fields(new[] { "title", "description" })
                        .Query(query));
                else
                    q.MatchAll();
            }), cancellationToken);

        return new SearchResult(
            response.Documents.ToList(),
            response.Total,
            page,
            pageSize);
    }

    public async Task IndexCatalogAsync(CatalogDocument catalog, CancellationToken cancellationToken = default)
        => await client.IndexAsync(catalog, i => i.Index(IndexName).Id(catalog.Id.ToString()), cancellationToken);
}
