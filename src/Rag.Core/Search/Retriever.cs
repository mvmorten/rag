using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.AI;

namespace Rag.Core.Search;

public enum SearchMode
{
    Keyword,
    Vector,
    Hybrid,
    HybridSemantic,
}

public sealed record RetrievedChunk(
    string Id,
    string Url,
    string Title,
    string SectionPath,
    string Content,
    double Score,
    double? RerankerScore);

public interface IRetriever
{
    Task<IReadOnlyList<RetrievedChunk>> SearchAsync(string query, SearchMode mode, int top, CancellationToken ct = default);
}

public sealed class Retriever(SearchClient search, IEmbeddingGenerator<string, Embedding<float>> embeddings) : IRetriever
{
    private static readonly string[] Fields = ["id", "url", "title", "sectionPath", "content"];

    public async Task<IReadOnlyList<RetrievedChunk>> SearchAsync(
        string query, SearchMode mode, int top, CancellationToken ct = default)
    {
        var options = new SearchOptions { Size = top };
        foreach (var f in Fields) options.Select.Add(f);

        var useText = mode != SearchMode.Vector;
        var useVector = mode != SearchMode.Keyword;

        if (useVector)
        {
            var vector = (await embeddings.GenerateAsync([query], cancellationToken: ct))[0].Vector;
            options.VectorSearch = new VectorSearchOptions
            {
                Queries = { new VectorizedQuery(vector) { KNearestNeighborsCount = 50, Fields = { "contentVector" } } },
            };
        }

        if (mode == SearchMode.HybridSemantic)
        {
            options.QueryType = SearchQueryType.Semantic;
            options.SemanticSearch = new SemanticSearchOptions { SemanticConfigurationName = IndexSchema.SemanticConfig };
        }

        var response = await search.SearchAsync<ChunkDocument>(useText ? query : null, options, ct);
        var results = new List<RetrievedChunk>();
        await foreach (var r in response.Value.GetResultsAsync().WithCancellation(ct))
        {
            var d = r.Document;
            results.Add(new RetrievedChunk(
                d.Id, d.Url, d.Title, d.SectionPath, d.Content, r.Score ?? 0, r.SemanticSearch?.RerankerScore));
            if (results.Count >= top) break;
        }
        return results;
    }
}
