using System.Net;
using BookingSystem.AiOrchestration.Chat;
using BookingSystem.AiOrchestration.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AiOrchestration.Tests;

/// <summary>Captures the outgoing request and returns a canned response — no real HTTP.</summary>
internal sealed class StubHandler(HttpStatusCode status = HttpStatusCode.OK, string body = "[]") : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        if (request.Content is not null)
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);

        return new HttpResponseMessage(status) { Content = new StringContent(body) };
    }
}

internal static class TestFactory
{
    public static IHttpClientFactory HttpFactory(HttpMessageHandler handler)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>())
            .Returns(_ => new HttpClient(handler) { BaseAddress = new Uri("http://gateway.local") });
        return factory;
    }

    public static BookingTools Tools(ProposalCapture capture, HttpMessageHandler handler, Guid demoUser)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Booking:DemoUserId"] = demoUser.ToString(),
            })
            .Build();

        return new BookingTools(HttpFactory(handler), capture, config, NullLogger<BookingTools>.Instance);
    }
}
