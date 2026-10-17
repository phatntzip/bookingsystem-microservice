using BookingSystem.ReviewService.Api.Features.CreateReview;
using BookingSystem.ReviewService.Infrastructure.Persistence;
using MediatR;

namespace BookingSystem.ReviewService.Api.Endpoints;

public static class ReviewEndpoints
{
    public static void MapReviewEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/reviews");

        group.MapPost("/", async (CreateReviewCommand cmd, ISender sender) =>
        {
            var id = await sender.Send(cmd);
            return Results.Created($"/api/reviews/{id}", new { id });
        })
        .RequireAuthorization()
        .WithName("CreateReview");

        group.MapGet("/catalog/{catalogId:guid}", async (Guid catalogId, IReviewRepository repo) =>
        {
            var reviews = await repo.GetByCatalogAsync(catalogId);
            return Results.Ok(reviews);
        })
        .WithName("GetReviewsByCatalog");
    }
}
