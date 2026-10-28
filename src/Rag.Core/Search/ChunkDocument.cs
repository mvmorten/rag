using System.Text.Json.Serialization;

namespace Rag.Core.Search;

/// <summary>A chunk as stored in the Azure AI Search index.</summary>
public sealed class ChunkDocument
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("sourcePath")] public string SourcePath { get; set; } = "";
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("sectionPath")] public string SectionPath { get; set; } = "";
    [JsonPropertyName("content")] public string Content { get; set; } = "";
    [JsonPropertyName("ordinal")] public int Ordinal { get; set; }
    [JsonPropertyName("contentHash")] public string ContentHash { get; set; } = "";
    [JsonPropertyName("lastUpdated")] public DateTimeOffset? LastUpdated { get; set; }
    [JsonPropertyName("contentVector")] public float[]? ContentVector { get; set; }
}
