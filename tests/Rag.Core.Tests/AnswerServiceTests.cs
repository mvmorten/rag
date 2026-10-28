using System.Text.Json;
using Microsoft.Extensions.AI;
using Rag.Core.Answering;
using Rag.Core.Search;

namespace Rag.Core.Tests;

public class AnswerServiceTests
{
    private static RetrievedChunk Chunk(int n) =>
        new($"id{n}", $"https://learn.microsoft.com/en-us/azure/container-apps/doc{n}", $"Doc {n}", $"Doc {n} > Section", $"content {n}", 1.0, null);

    private sealed class FakeRetriever(params RetrievedChunk[] chunks) : IRetriever
    {
        public Task<IReadOnlyList<RetrievedChunk>> SearchAsync(string query, SearchMode mode, int top, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RetrievedChunk>>(chunks);
    }

    private sealed class FakeChat(string json) : IChatClient
    {
        public int Calls { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, json)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private static string Json(bool answerable, string answer, params int[] citations) =>
        JsonSerializer.Serialize(new { answerable, answer, citations });

    [Fact]
    public async Task Returns_answer_with_resolved_citations()
    {
        var service = new AnswerService(new FakeRetriever(Chunk(1), Chunk(2), Chunk(3)), new FakeChat(Json(true, "Use minReplicas [2].", 2)));

        var result = await service.AskAsync("q");

        Assert.True(result.Answerable);
        var citation = Assert.Single(result.Citations);
        Assert.Equal(2, citation.Index);
        Assert.EndsWith("/doc2", citation.Url);
        Assert.Equal(3, result.Retrieved.Count);
    }

    [Fact]
    public async Task Refuses_when_model_says_not_answerable()
    {
        var service = new AnswerService(new FakeRetriever(Chunk(1)), new FakeChat(Json(false, "", 1)));

        var result = await service.AskAsync("q");

        Assert.False(result.Answerable);
        Assert.Equal(AnswerService.RefusalText, result.Answer);
        Assert.Empty(result.Citations);
    }

    [Fact]
    public async Task Refuses_an_answer_whose_citations_are_all_invalid()
    {
        var service = new AnswerService(new FakeRetriever(Chunk(1)), new FakeChat(Json(true, "Made up [9].", 9)));

        var result = await service.AskAsync("q");

        Assert.False(result.Answerable);
    }

    [Fact]
    public async Task Drops_invalid_citations_but_keeps_valid_ones()
    {
        var service = new AnswerService(new FakeRetriever(Chunk(1), Chunk(2)), new FakeChat(Json(true, "Fact [1] [7].", 1, 7, 1)));

        var result = await service.AskAsync("q");

        Assert.True(result.Answerable);
        Assert.Equal([1], result.Citations.Select(c => c.Index));
    }

    [Fact]
    public async Task Refuses_without_calling_the_model_when_nothing_is_retrieved()
    {
        var chat = new FakeChat(Json(true, "x", 1));
        var service = new AnswerService(new FakeRetriever(), chat);

        var result = await service.AskAsync("q");

        Assert.False(result.Answerable);
        Assert.Equal(0, chat.Calls);
    }

    [Fact]
    public async Task Refuses_when_model_output_is_not_valid_json()
    {
        var service = new AnswerService(new FakeRetriever(Chunk(1)), new FakeChat("not json"));

        var result = await service.AskAsync("q");

        Assert.False(result.Answerable);
    }
}
