using Rag.Eval;

namespace Rag.Core.Tests;

public class EvalTests
{
    private static string Url(string stem) => $"https://learn.microsoft.com/en-us/azure/container-apps/{stem}";

    [Fact]
    public void Rank_is_one_based_and_matches_on_doc_stem()
    {
        var urls = new[] { Url("billing"), Url("scale-app"), Url("jobs") };

        Assert.Equal(2, Metrics.FirstRelevantRank(urls, ["scale-app", "jobs"]));
        Assert.Null(Metrics.FirstRelevantRank(urls, ["cors"]));
    }

    private static readonly EvalSummary Base = new(0.95, 0.80, 0.95, 1.0, 0.95, 1.0);

    [Fact]
    public void Small_changes_within_tolerance_pass()
    {
        var current = Base with { RetrievalHitAt5 = 0.93, Answered = 0.90, Groundedness = 0.95 };

        Assert.True(Baseline.Compare(Base, current).Passed);
    }

    [Fact]
    public void Retrieval_drop_beyond_tolerance_fails()
    {
        var current = Base with { RetrievalHitAt5 = 0.85 };

        var (table, passed) = Baseline.Compare(Base, current);

        Assert.False(passed);
        Assert.Contains("REGRESSION", table);
    }

    [Fact]
    public void Any_drop_in_refusal_of_unanswerable_questions_fails()
    {
        Assert.False(Baseline.Compare(Base, Base with { RefusedUnanswerable = 0.67 }).Passed);
    }

    [Fact]
    public void Improvements_always_pass()
    {
        Assert.True(Baseline.Compare(Base, Base with { RetrievalMrr = 0.99, Answered = 1.0 }).Passed);
    }
}
