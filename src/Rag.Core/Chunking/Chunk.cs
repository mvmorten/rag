namespace Rag.Core.Chunking;

public sealed record Chunk(
    string Id,
    string SourcePath,
    string Title,
    string SectionPath,
    string Content,
    int Ordinal,
    DateTimeOffset? LastUpdated,
    string ContentHash);
