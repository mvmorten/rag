using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.AI;
using Rag.Core.Chunking;
using Rag.Core.Search;

namespace Rag.Ingest;

public sealed record IngestResult(int Files, int Chunks, int Embedded, int Unchanged, int Deleted);

/// <summary>
/// Incremental indexer. Compares each chunk's content hash with what is already in the index,
/// embeds and uploads only new or changed chunks, and removes chunks that no longer exist.
/// </summary>
public sealed class Indexer(
    SearchClient search,
    IEmbeddingGenerator<string, Embedding<float>> embeddings,
    MarkdownChunker chunker)
{
    private const int EmbedBatch = 16;
    private const int UploadBatch = 200;
    private const int PageSize = 1000;

    public async Task<IngestResult> RunAsync(string docsRoot, string relativeBase, CancellationToken ct = default)
    {
        var existing = await LoadExistingAsync(ct);
        var desired = new Dictionary<string, ChunkDocument>();
        var files = 0;

        foreach (var file in EnumerateDocs(docsRoot))
        {
            files++;
            var relative = Path.GetRelativePath(docsRoot, file).Replace('\\', '/');
            var sourcePath = $"{relativeBase}/{relative}";
            var markdown = await File.ReadAllTextAsync(file, ct);
            foreach (var c in chunker.Chunk(sourcePath, markdown))
            {
                desired[c.Id] = new ChunkDocument
                {
                    Id = c.Id,
                    SourcePath = c.SourcePath,
                    Url = DocsSource.ToUrl(c.SourcePath),
                    Title = c.Title,
                    SectionPath = c.SectionPath,
                    Content = c.Content,
                    Ordinal = c.Ordinal,
                    ContentHash = c.ContentHash,
                    LastUpdated = c.LastUpdated,
                };
            }
        }

        var changed = desired.Values
            .Where(d => !existing.TryGetValue(d.Id, out var hash) || hash != d.ContentHash)
            .ToList();
        var stale = existing.Keys.Where(id => !desired.ContainsKey(id)).ToList();

        Console.WriteLine($"{files} files -> {desired.Count} chunks; {changed.Count} to embed, {stale.Count} to delete.");

        for (var i = 0; i < changed.Count; i += EmbedBatch)
        {
            var batch = changed.Skip(i).Take(EmbedBatch).ToList();
            var vectors = await embeddings.GenerateAsync(batch.Select(EmbeddingText), cancellationToken: ct);
            for (var j = 0; j < batch.Count; j++) batch[j].ContentVector = vectors[j].Vector.ToArray();
            Console.Write($"\rembedded {Math.Min(i + EmbedBatch, changed.Count)}/{changed.Count}");
        }
        if (changed.Count > 0) Console.WriteLine();

        for (var i = 0; i < changed.Count; i += UploadBatch)
            await search.MergeOrUploadDocumentsAsync(changed.Skip(i).Take(UploadBatch), cancellationToken: ct);

        for (var i = 0; i < stale.Count; i += UploadBatch)
            await search.DeleteDocumentsAsync("id", stale.Skip(i).Take(UploadBatch), cancellationToken: ct);

        return new IngestResult(files, desired.Count, changed.Count, desired.Count - changed.Count, stale.Count);
    }

    /// <summary>The text that gets embedded: title and section give each chunk context a bare paragraph lacks.</summary>
    public static string EmbeddingText(ChunkDocument d) => $"{d.SectionPath}\n\n{d.Content}";

    private static IEnumerable<string> EnumerateDocs(string root) =>
        Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories)
            .Where(f =>
            {
                // `includes` holds reusable fragments, not standalone articles.
                var rel = Path.GetRelativePath(root, f).Replace('\\', '/');
                return !rel.StartsWith("includes/") && !rel.Contains("/includes/");
            })
            .Order();

    private async Task<Dictionary<string, string>> LoadExistingAsync(CancellationToken ct)
    {
        var map = new Dictionary<string, string>();
        // Explicit Top/Skip paging: the SDK's automatic continuation stops after the first 1000 results.
        for (var skip = 0; ; skip += PageSize)
        {
            var options = new SearchOptions { Size = PageSize, Skip = skip };
            options.Select.Add("id");
            options.Select.Add("contentHash");

            var response = await search.SearchAsync<ChunkDocument>("*", options, ct);
            var count = 0;
            await foreach (var r in response.Value.GetResultsAsync().WithCancellation(ct))
            {
                map[r.Document.Id] = r.Document.ContentHash;
                count++;
            }
            if (count < PageSize) return map;
        }
    }
}
