using System.Text;

namespace Rag.Eval;

public static class Metrics
{
    /// <summary>1-based rank of the first retrieved chunk whose URL points at an expected doc, or null.</summary>
    public static int? FirstRelevantRank(IReadOnlyList<string> urls, IReadOnlyCollection<string> expectedStems)
    {
        for (var i = 0; i < urls.Count; i++)
        {
            var stem = urls[i][(urls[i].LastIndexOf('/') + 1)..];
            if (expectedStems.Contains(stem, StringComparer.OrdinalIgnoreCase)) return i + 1;
        }
        return null;
    }

    public static double HitRate(IReadOnlyList<QuestionOutcome> o, int k) =>
        o.Count(x => x.Rank is { } r && r <= k) / (double)o.Count;

    public static double Mrr(IReadOnlyList<QuestionOutcome> o, int k) =>
        o.Average(x => x.Rank is { } r && r <= k ? 1.0 / r : 0.0);

    public static string RenderMarkdown(IReadOnlyList<ModeReport> report, int k)
    {
        var sb = new StringBuilder();
        var n = report[0].Outcomes.Count;
        sb.AppendLine($"Retrieval over {n} answerable questions (chunk-level, top {k}).");
        sb.AppendLine();
        sb.AppendLine("| Mode | hit@1 | hit@3 | hit@5 | MRR |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var m in report)
            sb.AppendLine($"| {m.Mode} | {HitRate(m.Outcomes, 1):P0} | {HitRate(m.Outcomes, 3):P0} | {HitRate(m.Outcomes, 5):P0} | {Mrr(m.Outcomes, k):F3} |");
        return sb.ToString();
    }
}
