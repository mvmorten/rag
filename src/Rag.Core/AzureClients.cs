using Azure.AI.OpenAI;
using Azure.Identity;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Microsoft.Extensions.AI;

namespace Rag.Core;

public sealed class AzureClients(RagSettings settings)
{
    private readonly DefaultAzureCredential _credential = new();

    public SearchIndexClient IndexClient => new(new Uri(settings.SearchEndpoint), _credential);

    public SearchClient SearchClient => new(new Uri(settings.SearchEndpoint), settings.SearchIndex, _credential);

    private AzureOpenAIClient OpenAI => new(new Uri(settings.OpenAIEndpoint), _credential);

    public IEmbeddingGenerator<string, Embedding<float>> Embeddings =>
        OpenAI.GetEmbeddingClient(settings.EmbeddingDeployment).AsIEmbeddingGenerator();

    public IChatClient Judge => OpenAI.GetChatClient(settings.JudgeDeployment).AsIChatClient();

    public IChatClient Chat => OpenAI.GetChatClient(settings.ChatDeployment).AsIChatClient();
}
