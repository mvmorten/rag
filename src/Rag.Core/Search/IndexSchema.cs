using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;

namespace Rag.Core.Search;

public static class IndexSchema
{
    public const string VectorProfile = "vec-profile";
    public const string SemanticConfig = "default";

    public static SearchIndex Build(RagSettings settings)
    {
        var index = new SearchIndex(settings.SearchIndex)
        {
            Fields =
            {
                new SimpleField("id", SearchFieldDataType.String) { IsKey = true },
                new SimpleField("sourcePath", SearchFieldDataType.String) { IsFilterable = true },
                new SimpleField("url", SearchFieldDataType.String),
                new SearchableField("title") { AnalyzerName = LexicalAnalyzerName.EnMicrosoft },
                new SearchableField("sectionPath") { AnalyzerName = LexicalAnalyzerName.EnMicrosoft },
                new SearchableField("content") { AnalyzerName = LexicalAnalyzerName.EnMicrosoft },
                new SimpleField("ordinal", SearchFieldDataType.Int32) { IsSortable = true },
                new SimpleField("contentHash", SearchFieldDataType.String),
                new SimpleField("lastUpdated", SearchFieldDataType.DateTimeOffset) { IsFilterable = true, IsSortable = true },
                new SearchField("contentVector", SearchFieldDataType.Collection(SearchFieldDataType.Single))
                {
                    IsSearchable = true,
                    VectorSearchDimensions = settings.EmbeddingDimensions,
                    VectorSearchProfileName = VectorProfile,
                },
            },
            VectorSearch = new VectorSearch
            {
                Algorithms = { new HnswAlgorithmConfiguration("hnsw") },
                Profiles = { new VectorSearchProfile(VectorProfile, "hnsw") },
            },
            SemanticSearch = new SemanticSearch
            {
                Configurations =
                {
                    new SemanticConfiguration(SemanticConfig, new SemanticPrioritizedFields
                    {
                        TitleField = new SemanticField("title"),
                        ContentFields = { new SemanticField("content") },
                        KeywordsFields = { new SemanticField("sectionPath") },
                    }),
                },
            },
        };
        return index;
    }
}
