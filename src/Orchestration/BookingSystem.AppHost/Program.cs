using BookingSystem.AppHost;
using BookingSystem.Shared.Contracts.Events;
using Confluent.Kafka;
using Confluent.Kafka.Admin;

var builder = DistributedApplication.CreateBuilder(args);

// --- Infrastructure ---
var v = InfraVersions.Load(builder.AppHostDirectory);

// Persistent database
var username = builder.AddParameter("username", "postgres", secret: true);
var password = builder.AddParameter("password", "postgres", secret: true);

var postgres = builder.AddPostgres("postgres")
.WithImageTag(v.Postgres)
.WithPgAdmin()
.WithDataVolume("booking-system-postgres-data")
.WithContainerName("booking-system-postgres")
.WithUserName(username).WithPassword(password);

var redis = builder.AddRedis("redis").WithImageTag(v.Redis).WithRedisCommander();
var kafka = builder.AddKafka("kafka").WithImageTag(v.Kafka).WithKafkaUI();
var elastic = builder.AddElasticsearch("elasticsearch").WithImageTag(v.Elasticsearch);

// Provision all Kafka topics with explicit settings when the broker becomes ready.
// Without this, topics are auto-created on first produce with Kafka defaults (1 partition,
// no replication, no custom retention) which is fine for dev but wrong for production.
builder.Eventing.Subscribe<ResourceReadyEvent>(kafka.Resource, async (@event, ct) =>
{
    var bootstrapServers = await kafka.Resource.ConnectionStringExpression.GetValueAsync(ct);
    if (bootstrapServers is null) return;

    using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrapServers }).Build();

    var businessPublishingTopics = new[]
    {
        KafkaTopics.BookingCreated,
        KafkaTopics.BookingCancelled,
        KafkaTopics.BookingConfirmationFailed,
        KafkaTopics.PaymentSucceeded,
        KafkaTopics.PaymentFailed,
        KafkaTopics.PaymentRefunded,
        KafkaTopics.PaymentRefundFailed,
    };

    // A matching ".dlq" dead-letter topic for each — consumers (KafkaConsumerBase) park
    // messages here after retries are exhausted. Keep the suffix in sync with that class.
    var topics = businessPublishingTopics
        .Concat(businessPublishingTopics.Select(name => $"{name}.dlq"))
        .ToArray();

    try
    {
        await admin.CreateTopicsAsync(topics.Select(name => new TopicSpecification
        {
            Name = name,
            NumPartitions = 1,
            ReplicationFactor = 1,
        }));
    }
    catch (CreateTopicsException ex)
    {
        // Ignore "topic already exists" — treat everything else as a real failure.
        var failures = ex.Results
            .Where(r => r.Error.IsError && r.Error.Code != ErrorCode.TopicAlreadyExists)
            .ToList();

        if (failures.Count > 0)
            throw new InvalidOperationException(
                $"Failed to create Kafka topics: {string.Join(", ", failures.Select(r => $"{r.Topic}: {r.Error.Reason}"))}",
                ex);
    }
});

// --- Databases (one per service) ---
var userDb = postgres.AddDatabase("userdb");
var catalogDb = postgres.AddDatabase("catalogdb");
var bookingDb = postgres.AddDatabase("bookingdb");
var paymentDb = postgres.AddDatabase("paymentdb");
var notifDb = postgres.AddDatabase("notifdb");
var reviewDb = postgres.AddDatabase("reviewdb");

// --- Core Services ---
var userSvc = builder.AddProject<Projects.BookingSystem_UserService_Api>("user-service")
    .WithReference(userDb)
    .WithReference(redis)
    .WaitFor(userDb);

var catalogSvc = builder.AddProject<Projects.BookingSystem_CatalogService_Api>("catalog-service")
    .WithReference(catalogDb)
    .WithReference(redis)
    .WaitFor(catalogDb);

var bookingSvc = builder.AddProject<Projects.BookingSystem_BookingService_Api>("booking-service")
    .WithReference(bookingDb)
    .WithReference(redis)
    .WithReference(kafka)
    .WithReference(userSvc)
    .WithReference(catalogSvc)
    .WaitFor(kafka)
    .WaitFor(bookingDb)
    .WaitFor(userSvc)
    .WaitFor(catalogSvc);

var paymentSvc = builder.AddProject<Projects.BookingSystem_PaymentService_Api>("payment-service")
    .WithReference(paymentDb)
    .WithReference(kafka)
    .WaitFor(kafka)
    .WaitFor(paymentDb);

var notifSvc = builder.AddProject<Projects.BookingSystem_NotificationService_Api>("notification-service")
    .WithReference(notifDb)
    .WithReference(kafka)
    .WithReference(redis)
    .WaitFor(kafka)
    .WaitFor(notifDb);

var searchSvc = builder.AddProject<Projects.BookingSystem_SearchService_Api>("search-service")
    .WithReference(elastic)
    .WithReference(redis)
    .WithReference(kafka);

var reviewSvc = builder.AddProject<Projects.BookingSystem_ReviewService_Api>("review-service")
    .WithReference(reviewDb)
    .WithReference(kafka)
    .WaitFor(reviewDb)
    .WaitFor(kafka);

// --- API Gateway ---
var gateway = builder.AddProject<Projects.BookingSystem_ApiGateway>("api-gateway")
    .WithReference(userSvc)
    .WithReference(catalogSvc)
    .WithReference(bookingSvc)
    .WithReference(paymentSvc)
    .WithReference(searchSvc)
    .WithReference(reviewSvc)
    .WithExternalHttpEndpoints();

// --- AI Orchestration (natural-language layer over the gateway) ---
// Talks to the gateway via service discovery ("http://api-gateway"). Needs an Anthropic API key:
// set ANTHROPIC_API_KEY in the AppHost environment and it is forwarded to the service.
builder.AddProject<Projects.BookingSystem_AiOrchestration>("ai-orchestration")
    .WithReference(gateway)
    .WaitFor(gateway)
    .WithEnvironment("ANTHROPIC_API_KEY", builder.Configuration["ANTHROPIC_API_KEY"] ?? "")
    .WithExternalHttpEndpoints();

builder.Build().Run();
