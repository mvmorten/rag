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

var app = builder.Build();
app.UseCors();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/search", async (string q, SearchMode? mode, int? k, IRetriever retriever, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(q)) return Results.BadRequest(new { error = "q is required" });
    var results = await retriever.SearchAsync(q, mode ?? SearchMode.Hybrid, Math.Clamp(k ?? 5, 1, 20), ct);
    return Results.Ok(results);
});

app.MapPost("/ask", async (AskRequest req, AnswerService answers, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Question)) return Results.BadRequest(new { error = "question is required" });
    if (req.Question.Length > 1000) return Results.BadRequest(new { error = "question is too long (max 1000 characters)" });
    var result = await answers.AskAsync(req.Question, req.Mode ?? SearchMode.Hybrid, Math.Clamp(req.K ?? 6, 1, 12), ct);
    return Results.Ok(result);
});

app.Run();

public sealed record AskRequest(string Question, SearchMode? Mode, int? K);

public partial class Program;
