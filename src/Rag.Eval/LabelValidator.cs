namespace Rag.Eval;

/// <summary>Checks that every labelled question points at real docs that contain the stated phrases.</summary>
public static class LabelValidator
{
    public static List<string> Validate(IEnumerable<EvalQuestion> questions, string docsDir)
    {
        var problems = new List<string>();
        foreach (var q in questions)
        {
            if (!q.Answerable) continue;
            if (q.Expected is not { Count: > 0 }) { problems.Add($"{q.Id}: answerable but no expected docs"); continue; }

            var texts = new List<string>();
            foreach (var stem in q.Expected)
            {
                var path = Path.Combine(docsDir, stem + ".md");
                if (File.Exists(path)) texts.Add(File.ReadAllText(path));
                else problems.Add($"{q.Id}: expected doc '{stem}.md' not found");
            }

            foreach (var phrase in q.MustContain ?? [])
                if (texts.Count > 0 && !texts.Any(t => t.Contains(phrase, StringComparison.OrdinalIgnoreCase)))
                    problems.Add($"{q.Id}: none of the expected docs contain '{phrase}'");
        }
        return problems;
    }
}
