using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using Rag.Core.Answering;
using Rag.Core.Search;

namespace Rag.Mcp;

[McpServerToolType]
public sealed class DocsTools(IRetriever retriever, AnswerService answers)
{
    [McpServerTool(Name = "search_docs", ReadOnly = true)]
    [Description("Search the Azure Container Apps documentation and return the most relevant passages with their source URLs. Use this when you need the raw documentation text.")]
    public async Task<string> SearchDocs(
        [Description("What to look for, as a natural-language question or keywords.")] string query,
        [Description("Number of passages to return (1-10).")] int top = 5,
        [Description("Retrieval mode: Hybrid (default), HybridSemantic, Vector or Keyword.")] SearchMode mode = SearchMode.Hybrid,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return "query is required.";

        var results = await retriever.SearchAsync(query, mode, Math.Clamp(top, 1, 10), ct);
        if (results.Count == 0) return "No passages found.";

        var sb = new StringBuilder();
        for (var i = 0; i < results.Count; i++)
        {
            var r = results[i];
            sb.AppendLine($"[{i + 1}] {r.SectionPath}");
            sb.AppendLine($"URL: {r.Url}");
            sb.AppendLine($"Score: {r.Score:F3}");
            sb.AppendLine(r.Content);
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    [McpServerTool(Name = "ask_docs", ReadOnly = true)]
    [Description("Ask a question about Azure Container Apps and get a short answer grounded in the documentation, with citations. Refuses when the documentation does not contain the answer.")]
    public async Task<string> AskDocs(
        [Description("The question to answer.")] string question,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(question)) return "question is required.";
        if (question.Length > 1000) return "question is too long (max 1000 characters).";

        var result = await answers.AskAsync(question, ct: ct);
        if (!result.Answerable) return $"{result.Answer} (reason: {result.Refusal})";

        var sb = new StringBuilder(result.Answer).AppendLine().AppendLine().AppendLine("Sources:");
        foreach (var c in result.Citations) sb.AppendLine($"[{c.Index}] {c.SectionPath} - {c.Url}");
        return sb.ToString().TrimEnd();
    }
}
