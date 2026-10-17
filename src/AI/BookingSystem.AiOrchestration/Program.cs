using Anthropic;
using BookingSystem.AiOrchestration.Chat;
using BookingSystem.AiOrchestration.Tools;
using BookingSystem.ServiceDefaults;
using Microsoft.Extensions.AI;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// --- Anthropic (Claude) chat client, exposed through Microsoft.Extensions.AI ---
// The provider is swappable: any IChatClient (OpenAI, Azure OpenAI, Ollama) can replace
// this block without touching the tools or the confirmation gate below.
var apiKey = builder.Configuration["Anthropic:ApiKey"]
             ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
var anthropic = string.IsNullOrEmpty(apiKey)
    ? new AnthropicClient()                       // reads ANTHROPIC_API_KEY from the environment
    : new AnthropicClient { ApiKey = apiKey };

var model = builder.Configuration["Anthropic:Model"] ?? "claude-opus-4-8";
var maxTokens = builder.Configuration.GetValue<int?>("Anthropic:MaxOutputTokens") ?? 16000;

IChatClient chatClient = anthropic
    .AsIChatClient(model, maxTokens)
    .AsBuilder()
    .UseFunctionInvocation()                      // auto-invokes the read tools; write tools only capture
    .Build();

builder.Services.AddSingleton(chatClient);
builder.Services.AddSingleton(PromptCacheOptions.From(builder.Configuration));

// --- HTTP to the existing API gateway ---
// Under Aspire, service discovery resolves "http://api-gateway". Standalone, override with
// Gateway:BaseUrl (e.g. the gateway's localhost URL).
var gatewayUrl = builder.Configuration["Gateway:BaseUrl"] ?? "http://api-gateway";
builder.Services.AddHttpClient("gateway", c => c.BaseAddress = new Uri(gatewayUrl));

// --- Orchestration state + tools ---
builder.Services.AddScoped<ProposalCapture>();    // per-turn: catches a proposed write
builder.Services.AddScoped<BookingTools>();
builder.Services.AddSingleton<ProposalStore>();   // cross-request: proposals awaiting confirmation
builder.Services.AddSingleton<ConversationStore>();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapChatEndpoints();

app.Run();
