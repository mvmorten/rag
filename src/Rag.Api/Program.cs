using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Rag.Core;
using Rag.Core.Answering;
using Rag.Core.Search;

var builder = WebApplication.CreateBuilder(args);

var settings = builder.Configuration.GetSection("Rag").Get<RagSettings>()
    ?? throw new InvalidOperationException("Missing 'Rag' configuration section.");
var azure = new AzureClients(settings);

builder.Services.AddSingleton(settings);
builder.Services.AddSingleton<IRetriever>(new Retriever(azure.SearchClient, azure.Embeddings));
builder.Services.AddSingleton(azure.Chat);
builder.Services.AddSingleton<AnswerService>();
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"])
     .AllowAnyHeader().AllowAnyMethod()));

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

// The API is public and every /ask spends model tokens, so cap requests per client IP.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("per-ip", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1) }));
});

// Behind the container app's ingress the client IP arrives in X-Forwarded-For; without this every caller shares one rate-limit bucket.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();
app.UseForwardedHeaders();

// Behind the container app the UI and API share one origin and the UI calls /api/*, matching the Vite dev proxy.
app.UsePathBase("/api");
app.UseRouting();
app.UseCors();
app.UseRateLimiter();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/search", async (string q, SearchMode? mode, int? k, IRetriever retriever, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(q)) return Results.BadRequest(new { error = "q is required" });
    var results = await retriever.SearchAsync(q, mode ?? SearchMode.Hybrid, Math.Clamp(k ?? 5, 1, 20), ct);
    return Results.Ok(results);
}).RequireRateLimiting("per-ip");

app.MapPost("/ask", async (AskRequest req, AnswerService answers, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Question)) return Results.BadRequest(new { error = "question is required" });
    if (req.Question.Length > 1000) return Results.BadRequest(new { error = "question is too long (max 1000 characters)" });
    var result = await answers.AskAsync(req.Question, req.Mode ?? SearchMode.Hybrid, Math.Clamp(req.K ?? 6, 1, 12), ct);
    return Results.Ok(result);
}).RequireRateLimiting("per-ip");

// Serve the built UI when it is present (the container image copies it to wwwroot).
if (Directory.Exists(Path.Combine(app.Environment.ContentRootPath, "wwwroot")))
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

app.Run();

public sealed record AskRequest(string Question, SearchMode? Mode, int? K);

public partial class Program;
