using System.Text;

namespace Rag.Eval;

/// <summary>The handful of numbers CI tracks. Retrieval figures are for hybrid search.</summary>
public sealed record EvalSummary(
    double RetrievalHitAt5,
    double RetrievalMrr,
    double Answered,
    double RefusedUnanswerable,
    double CitationAccuracy,
    double Groundedness)
{
    public static EvalSummary From(ModeReport hybrid, IReadOnlyList<AnswerOutcome> answers)
    {
        double Rate(IEnumerable<bool?> values)
        {
            var v = values.Where(x => x is not null).ToList();
            return v.Count == 0 ? 0 : v.Count(x => x == true) / (double)v.Count;
        }

        var answerable = answers.Where(a => a.ExpectedAnswerable).ToList();
        var unanswerable = answers.Where(a => !a.ExpectedAnswerable).ToList();
        return new EvalSummary(
            Metrics.HitRate(hybrid.Outcomes, 5),
            Metrics.Mrr(hybrid.Outcomes, 10),
            Rate(answerable.Select(a => (bool?)a.Answered)),
            Rate(unanswerable.Select(a => (bool?)!a.Answered)),
            Rate(answerable.Select(a => a.CitationHit)),
            Rate(answers.Where(a => a.Answered).Select(a => a.Grounded)));
    }
}

/// <summary>
/// Compares a run against a committed baseline. Tolerances absorb run-to-run noise (small question
/// set, unpinned model temperature); a drop beyond them fails CI.
/// </summary>
public static class Baseline
{
    private sealed record Metric(string Name, Func<EvalSummary, double> Get, double Tolerance);

    private static readonly Metric[] Metrics =
    [
        new("Retrieval hit@5 (hybrid)", s => s.RetrievalHitAt5, 0.05),
        new("Retrieval MRR (hybrid)", s => s.RetrievalMrr, 0.05),
        new("Answered when answerable", s => s.Answered, 0.10),
        new("Refused when unanswerable", s => s.RefusedUnanswerable, 0.0),
        new("Citation accuracy", s => s.CitationAccuracy, 0.10),
        new("Groundedness (judge)", s => s.Groundedness, 0.10),
    ];

    public static (string Markdown, bool Passed) Compare(EvalSummary baseline, EvalSummary current)
    {
        var sb = new StringBuilder();
        sb.AppendLine("| Metric | Baseline | This run | Change | Status |");
        sb.AppendLine("|---|---|---|---|---|");
        var passed = true;
        foreach (var m in Metrics)
        {
            var b = m.Get(baseline);
            var c = m.Get(current);
            var delta = c - b;
            var ok = delta >= -m.Tolerance - 1e-9;
            passed &= ok;
            sb.AppendLine($"| {m.Name} | {b:P1} | {c:P1} | {delta * 100:+0.0;-0.0;0.0} pts | {(ok ? "ok" : $"**REGRESSION** (> {m.Tolerance * 100:0} pts)")} |");
        }
        return (sb.ToString(), passed);
    }
}
