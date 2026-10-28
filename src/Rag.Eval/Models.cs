namespace Rag.Eval;

public sealed class EvalQuestion
{
    public string Id { get; set; } = "";
    public string Question { get; set; } = "";
    public bool Answerable { get; set; } = true;

    /// <summary>Doc stems (file name without .md) that contain the answer. Any one counts as a hit.</summary>
    public List<string>? Expected { get; set; }

    /// <summary>Phrases the expected docs must contain, so labels can be checked against the corpus.</summary>
    public List<string>? MustContain { get; set; }
}

public sealed record QuestionOutcome(string Id, int? Rank, IReadOnlyList<string> RetrievedUrls);

public sealed record ModeReport(string Mode, IReadOnlyList<QuestionOutcome> Outcomes);
