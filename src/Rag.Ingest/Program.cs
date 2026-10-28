using Azure.Search.Documents.Indexes;
using Microsoft.Extensions.Configuration;
using Rag.Core;
using Rag.Core.Chunking;
using Rag.Core.Search;
using Rag.Ingest;

// Usage: dotnet run --project src/Rag.Ingest [--dry-run] [--docs-dir <path>] [--folder articles/container-apps]
var argv = args.ToList();
string? Opt(string name) => argv.IndexOf(name) is var i and >= 0 && i + 1 < argv.Count ? argv[i + 1] : null;

var dryRun = argv.Contains("--dry-run");
var folder = Opt("--folder") ?? "articles/container-apps";
var cloneDir = Path.GetFullPath(Opt("--docs-dir") ?? Path.Combine("data", "azure-docs"));

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddEnvironmentVariables("RAG_")
    .Build();
var settings = config.GetSection("Rag").Get<RagSettings>()
    ?? throw new InvalidOperationException("Missing 'Rag' configuration section.");

Console.WriteLine($"Fetching {folder} ...");
var docsRoot = await DocsSource.EnsureAsync(cloneDir, folder);
var chunker = new MarkdownChunker();

if (dryRun)
{
    var chunks = Directory.EnumerateFiles(docsRoot, "*.md", SearchOption.AllDirectories)
        .Where(f => !f.Replace('\\', '/').Contains("/includes/"))
        .SelectMany(f => chunker.Chunk(f, File.ReadAllText(f)))
        .ToList();
    var sizes = chunks.Select(c => c.Content.Length).Order().ToList();
    Console.WriteLine($"{chunks.Count} chunks; length median {sizes[sizes.Count / 2]}, p95 {sizes[(int)(sizes.Count * 0.95)]}, max {sizes[^1]}");
    return;
}

var azure = new AzureClients(settings);
await azure.IndexClient.CreateOrUpdateIndexAsync(IndexSchema.Build(settings));
Console.WriteLine($"Index '{settings.SearchIndex}' ready.");

var indexer = new Indexer(azure.SearchClient, azure.Embeddings, chunker);
var result = await indexer.RunAsync(docsRoot, folder);
Console.WriteLine($"Done: {result}");
