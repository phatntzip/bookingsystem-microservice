using BookingSystem.CatalogService.Api.Endpoints;
using BookingSystem.Shared.Persistence;
using BookingSystem.CatalogService.Infrastructure.Persistence;
using BookingSystem.CatalogService.Infrastructure.Repositories;
using BookingSystem.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<CatalogDbContext>("catalogdb");
builder.AddRedisDistributedCache("redis");

builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));

builder.Services.AddScoped<ICatalogRepository, CatalogRepository>();

var app = builder.Build();

if (app.Configuration.GetValue<bool>("RunMigrationsOnStartup"))
{
    using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<CatalogDbContext>>();
    await db.MigrateWithRetryAsync(logger, attempts: 5, delay: TimeSpan.FromSeconds(2));
}

app.MapDefaultEndpoints();
app.MapCatalogEndpoints();

app.Run();
