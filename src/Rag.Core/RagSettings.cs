namespace Rag.Core;

/// <summary>Non-secret endpoint and deployment names. Auth is always Entra ID via DefaultAzureCredential.</summary>
public sealed class RagSettings
{
    public required string OpenAIEndpoint { get; init; }
    public required string SearchEndpoint { get; init; }
    public string SearchIndex { get; init; } = "container-apps-docs";
    public string EmbeddingDeployment { get; init; } = "text-embedding-3-small";
    public string ChatDeployment { get; init; } = "gpt-5.4-mini";
    public int EmbeddingDimensions { get; init; } = 1536;
}
