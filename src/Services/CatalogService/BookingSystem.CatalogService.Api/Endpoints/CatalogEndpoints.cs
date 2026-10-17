using BookingSystem.CatalogService.Api.Features.GetCatalog;
using BookingSystem.CatalogService.Api.Features.CreateCatalog;
using BookingSystem.CatalogService.Infrastructure.Repositories;
using BookingSystem.Shared.Contracts.DTOs;
using MediatR;

namespace BookingSystem.CatalogService.Api.Endpoints;

public static class CatalogEndpoints
{
    public static void MapCatalogEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/catalog");

        group.MapGet("/catalogs", async (ICatalogRepository repo, CancellationToken ct) =>
        {
            var catalogs = await repo.GetAllAsync(ct);
            var dtos = catalogs.Select(c => new CatalogDto(
                c.Id, c.Title, c.Description, c.PricePerNight, c.Currency, c.IsAvailable));
            return Results.Ok(dtos);
        })
        .WithName("ListCatalogs");

        group.MapGet("/catalogs/search", async (string? name, ICatalogRepository repo, CancellationToken ct) =>
        {
            var matches = await repo.SearchByNameAsync(name ?? string.Empty, ct);
            var dtos = matches.Select(c => new CatalogDto(
                c.Id, c.Title, c.Description, c.PricePerNight, c.Currency, c.IsAvailable));
            return Results.Ok(dtos);
        })
        .WithName("SearchCatalogs");

        group.MapGet("/catalogs/{id:guid}", async (Guid id, ISender sender) =>
        {
            var catalog = await sender.Send(new GetCatalogByIdQuery(id));
            return catalog is null ? Results.NotFound() : Results.Ok(catalog);
        })
        .WithName("GetCatalogById");

        group.MapPost("/catalogs", async (CreateCatalogCommand cmd, ISender sender) =>
        {
            var id = await sender.Send(cmd);
            return Results.Created($"/api/catalog/catalogs/{id}", new { id });
        })
        .WithName("CreateCatalog");
    }
}
