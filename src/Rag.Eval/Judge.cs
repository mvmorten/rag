using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.AI;
using Rag.Core.Search;

namespace Rag.Eval;

public sealed record JudgeVerdict(
    [property: Description("true only if every factual claim in the answer is supported by the sources")] bool Grounded,
    [property: Description("true if the answer actually addresses the question that was asked")] bool AnswersQuestion,
    [property: Description("claims in the answer that the sources do not support; empty if none")] string[] UnsupportedClaims);

/// <summary>LLM-as-judge: checks an answer against the exact sources the generator saw.</summary>
public sealed class Judge(IChatClient chat)
{
    private const string System = """
        You are a strict evaluator of a documentation Q&A system.
        You get a question, the numbered sources that were given to the system, and the system's answer.
        Judge ONLY against the sources, never against your own knowledge.
        - grounded: every factual claim in the answer is supported by the sources. Extra claims that no source states make it false.
        - answersQuestion: the answer addresses what the question asked.
        - unsupportedClaims: list any claims lacking support.
        """;

    public async Task<JudgeVerdict?> JudgeAsync(
        string question, string answer, IReadOnlyList<RetrievedChunk> sources, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Sources:");
        for (var i = 0; i < sources.Count; i++)
            sb.AppendLine($"[{i + 1}] {sources[i].SectionPath}\n{sources[i].Content}\n");
        sb.AppendLine($"Question: {question}\n\nAnswer:\n{answer}");

        var response = await chat.GetResponseAsync<JudgeVerdict>(
            [new ChatMessage(ChatRole.System, System), new ChatMessage(ChatRole.User, sb.ToString())],
            cancellationToken: ct);
        return response.TryGetResult(out var verdict) ? verdict : null;
    }
}
