using BookingSystem.SearchService.Infrastructure.Search;
using MediatR;

namespace BookingSystem.SearchService.Api.Features.SearchCatalogs;

public record SearchCatalogsQuery(
    string? Query,
    DateOnly? CheckIn,
    DateOnly? CheckOut,
    decimal? MaxPrice,
    int Page = 1,
    int PageSize = 20) : IRequest<SearchResult>;

public class SearchCatalogsHandler(ISearchService searchService)
    : IRequestHandler<SearchCatalogsQuery, SearchResult>
{
    public Task<SearchResult> Handle(SearchCatalogsQuery q, CancellationToken cancellationToken)
        => searchService.SearchCatalogsAsync(
            q.Query, q.CheckIn, q.CheckOut, q.MaxPrice, q.Page, q.PageSize, cancellationToken);
}
