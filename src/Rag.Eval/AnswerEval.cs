using System.Text;
using Rag.Core.Answering;
using Rag.Core.Search;

namespace Rag.Eval;

public sealed record AnswerOutcome(
    string Id,
    string Question,
    bool ExpectedAnswerable,
    bool Answered,
    string Answer,
    bool? CitationHit,
    bool? Grounded,
    bool? AnswersQuestion,
    IReadOnlyList<string> UnsupportedClaims,
    IReadOnlyList<string> CitedUrls,
    string Refusal);

public static class AnswerEval
{
    public static async Task<List<AnswerOutcome>> RunAsync(
        IEnumerable<EvalQuestion> questions, AnswerService answers, Judge judge, SearchMode mode, CancellationToken ct = default)
    {
        var outcomes = new List<AnswerOutcome>();
        foreach (var q in questions)
        {
            var result = await answers.AskAsync(q.Question, mode, ct: ct);

            bool? citationHit = null, grounded = null, answersQuestion = null;
            IReadOnlyList<string> unsupported = [];

            if (result.Answerable)
            {
                if (q.Answerable)
                    citationHit = result.Citations.Any(c => Metrics.FirstRelevantRank([c.Url], q.Expected!) is not null);

                // Judge against everything the generator saw, so groundedness is about the answer, not retrieval.
                var verdict = await judge.JudgeAsync(q.Question, result.Answer, result.Retrieved, ct);
                grounded = verdict?.Grounded;
                answersQuestion = verdict?.AnswersQuestion;
                unsupported = verdict?.UnsupportedClaims ?? [];
            }

            outcomes.Add(new AnswerOutcome(
                q.Id, q.Question, q.Answerable, result.Answerable, result.Answer,
                citationHit, grounded, answersQuestion, unsupported,
                result.Citations.Select(c => c.Url).ToList(),
                result.Refusal.ToString()));
            Console.WriteLine($"{q.Id}: {(result.Answerable ? "answered" : "refused (" + result.Refusal + ")")}");
        }
        return outcomes;
    }

    public static string RenderMarkdown(IReadOnlyList<AnswerOutcome> o, SearchMode mode)
    {
        var answerable = o.Where(x => x.ExpectedAnswerable).ToList();
        var unanswerable = o.Where(x => !x.ExpectedAnswerable).ToList();
        var answered = o.Where(x => x.Answered).ToList();

        string Pct(int n, int d) => d == 0 ? "n/a" : $"{n / (double)d:P0} ({n}/{d})";

        var sb = new StringBuilder();
        sb.AppendLine($"Answer quality, retrieval mode {mode}.");
        sb.AppendLine();
        sb.AppendLine("| Metric | Result |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| Answered when answerable | {Pct(answerable.Count(x => x.Answered), answerable.Count)} |");
        sb.AppendLine($"| Refused when unanswerable | {Pct(unanswerable.Count(x => !x.Answered), unanswerable.Count)} |");
        sb.AppendLine($"| Citation accuracy (cites an expected doc) | {Pct(answerable.Count(x => x.CitationHit == true), answerable.Count(x => x.CitationHit is not null))} |");
        sb.AppendLine($"| Groundedness (judge) | {Pct(answered.Count(x => x.Grounded == true), answered.Count(x => x.Grounded is not null))} |");
        sb.AppendLine($"| Answers the question (judge) | {Pct(answered.Count(x => x.AnswersQuestion == true), answered.Count(x => x.AnswersQuestion is not null))} |");
        return sb.ToString();
    }
}
