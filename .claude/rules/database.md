# Database Conventions

## DbContext registration

Always use the Aspire-aware extension, not the raw `AddDbContext`:

```csharp
builder.AddNpgsqlDbContext<UserDbContext>("userdb");
```

The string argument must match the database name declared in `AppHost/Program.cs` (e.g., `postgres.AddDatabase("userdb")`).

## DbContext naming

`{ServiceName}DbContext` in the Infrastructure project's `Persistence/` folder.  
EF `DbSet` properties use plural noun names matching the entity.

```csharp
public class UserDbContext(DbContextOptions<UserDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.ApplyConfigurationsFromAssembly(typeof(UserDbContext).Assembly); // OR inline below
    }
}
```

## Entity configuration approaches

**Inline `OnModelCreating`** — for simple services (UserService, CatalogService):

```csharp
protected override void OnModelCreating(ModelBuilder mb)
{
    mb.Entity<User>(e =>
    {
        e.HasKey(u => u.Id);
        e.Property(u => u.Email).HasMaxLength(256).IsRequired();
        e.HasIndex(u => u.Email).IsUnique();
        e.ToTable("users");
    });
}
```

**`IEntityTypeConfiguration<T>`** — required for complex entities (BookingService):

```csharp
public class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.HasKey(b => b.Id);
        // ... full config
    }
}
```

When using `IEntityTypeConfiguration`, call `mb.ApplyConfigurationsFromAssembly(...)` in `OnModelCreating`. Do not mix both styles in the same DbContext.

## Value object mapping

**Strongly-typed ID records** — use `HasConversion`:
```csharp
builder.Property(b => b.Id)
    .HasConversion(id => id.Value, v => new BookingId(v));
```

**Owned complex value objects** — use `OwnsOne` with explicit column names:
```csharp
builder.OwnsOne(b => b.Period, p =>
{
    p.Property(x => x.CheckIn).HasColumnName("check_in");
    p.Property(x => x.CheckOut).HasColumnName("check_out");
    p.HasIndex(x => x.CheckIn).HasDatabaseName("IX_bookings_check_in");
});

builder.OwnsOne(b => b.TotalPrice, m =>
{
    m.Property(x => x.Amount).HasColumnName("price_amount").HasColumnType("decimal(18,2)");
    m.Property(x => x.Currency).HasColumnName("price_currency").HasMaxLength(3);
});
```

**Enums** — always store as string:
```csharp
builder.Property(b => b.Status).HasConversion<string>().HasMaxLength(20);
```

## Column and table naming

Use `snake_case` for table and column names (`ToTable("users")`, `HasColumnName("check_in")`). EF's default PascalCase column names are only acceptable in throw-away migrations; fix them before committing.

## Migrations

Every service with a database must have an `IDesignTimeDbContextFactory<T>` in its Infrastructure project so `dotnet ef` works without a running app:

```csharp
public class BookingDbContextFactory : IDesignTimeDbContextFactory<BookingDbContext>
{
    public BookingDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<BookingDbContext>()
            .UseNpgsql(DesignTimeConnectionString.Resolve("bookingdb", "BookingSystem.BookingService.Api"))
            .Options;
        return new BookingDbContext(options);
    }
}
```

`DesignTimeConnectionString.Resolve` (in `BookingSystem.Shared.CrossCutting`, referenced by every Infrastructure project) resolves the connection string in priority order: the `ConnectionStrings__{name}` environment variable Aspire injects, then the `ConnectionStrings:{name}` entry in the API project's `appsettings.json` (the local docker-compose Postgres). It throws if neither is present. Because the fallback targets `localhost:5432`, bring infra up first (`docker compose -f docker/docker-compose.infra.yml up -d`) before running `dotnet ef`.

Run migrations from the Infrastructure project (not the Api project):

```powershell
dotnet ef migrations add <Name> --project src/Services/{Name}Service/BookingSystem.{Name}Service.Infrastructure
dotnet ef database update  --project src/Services/{Name}Service/BookingSystem.{Name}Service.Infrastructure
```

All services with a database have migrations: BookingService, CatalogService, PaymentService, NotificationService, UserService, ReviewService.

## Auto-migration at startup

Controlled by `RunMigrationsOnStartup` in `appsettings.json`. Uses `MigrateWithRetryAsync` from `BookingSystem.Shared.Persistence` with 5 attempts and exponential backoff. Pattern:

```csharp
if (app.Configuration.GetValue<bool>("RunMigrationsOnStartup"))
{
    using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<BookingDbContext>>();
    await db.MigrateWithRetryAsync(logger, attempts: 5, delay: TimeSpan.FromSeconds(2));
}
```

## Redis key patterns

Each service owns its own key namespace. Do not write to another service's keys.

| Service | Key pattern | TTL |
|---|---|---|
| BookingService | `lock:catalog:{id}:{date}` | 30 s (Redlock) |
| CatalogService | `catalog:{id}` | 5 min |
| SearchService | `search:{hash}` | 2 min |
| UserService | `user:{id}:profile` | 10 min |
