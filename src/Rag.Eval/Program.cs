using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Rag.Core;
using Rag.Core.Search;
using Rag.Eval;

// Usage:
//   dotnet run --project src/Rag.Eval -- validate [--docs-dir data/azure-docs]
//   dotnet run --project src/Rag.Eval -- run [--k 10] [--out eval/results]
//   dotnet run --project src/Rag.Eval -- answers [--mode Hybrid] [--out eval/results]
//   dotnet run --project src/Rag.Eval -- ci [--baseline eval/baseline.json] [--update-baseline]
var argv = args.ToList();
string? Opt(string name) => argv.IndexOf(name) is var i and >= 0 && i + 1 < argv.Count ? argv[i + 1] : null;

var command = argv.FirstOrDefault() ?? "run";
var questionsPath = Opt("--questions") ?? Path.Combine("eval", "questions.json");
var questions = JsonSerializer.Deserialize<List<EvalQuestion>>(
    await File.ReadAllTextAsync(questionsPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

if (command == "validate")
{
    var docsDir = Path.Combine(Opt("--docs-dir") ?? Path.Combine("data", "azure-docs"), "articles", "container-apps");
    var problems = LabelValidator.Validate(questions, docsDir);
    problems.ForEach(Console.Error.WriteLine);
    Console.WriteLine(problems.Count == 0
        ? $"OK: {questions.Count} questions validated against {docsDir}"
        : $"{problems.Count} problem(s)");
    return problems.Count == 0 ? 0 : 1;
}

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddEnvironmentVariables("RAG_")
    .Build();
var settings = config.GetSection("Rag").Get<RagSettings>()!;
var azure = new AzureClients(settings);
var retriever = new Retriever(azure.SearchClient, azure.Embeddings);

if (command == "answers")
{
    var answerMode = Enum.Parse<SearchMode>(Opt("--mode") ?? nameof(SearchMode.Hybrid), ignoreCase: true);
    var service = new Rag.Core.Answering.AnswerService(retriever, azure.Chat);
    var outcomes = await AnswerEval.RunAsync(questions, service, new Judge(azure.Judge), answerMode);
    var md = AnswerEval.RenderMarkdown(outcomes, answerMode);
    Console.WriteLine();
    Console.WriteLine(md);

    var dir = Opt("--out") ?? Path.Combine("eval", "results");
    Directory.CreateDirectory(dir);
    var ts = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
    await File.WriteAllTextAsync(Path.Combine(dir, $"answers-{ts}.json"),
        JsonSerializer.Serialize(outcomes, new JsonSerializerOptions { WriteIndented = true }));
    await File.WriteAllTextAsync(Path.Combine(dir, $"answers-{ts}.md"), md, new UTF8Encoding(false));
    return 0;
}

async Task<List<ModeReport>> RunRetrievalAsync(int topK, IEnumerable<SearchMode> modes)
{
    var reports = new List<ModeReport>();
    foreach (var mode in modes)
    {
        var outcomes = new List<QuestionOutcome>();
        foreach (var q in questions.Where(q => q.Answerable))
        {
            var results = await retriever.SearchAsync(q.Question, mode, topK);
            var urls = results.Select(r => r.Url).ToList();
            outcomes.Add(new QuestionOutcome(q.Id, Metrics.FirstRelevantRank(urls, q.Expected!), urls));
        }
        reports.Add(new ModeReport(mode.ToString(), outcomes));
        Console.WriteLine($"{mode}: done");
    }
    return reports;
}

if (command == "ci")
{
    var baselinePath = Opt("--baseline") ?? Path.Combine("eval", "baseline.json");
    var jsonOptions = new JsonSerializerOptions { WriteIndented = true };

    var retrievalReport = await RunRetrievalAsync(10, Enum.GetValues<SearchMode>());
    var ciService = new Rag.Core.Answering.AnswerService(retriever, azure.Chat);
    var answerOutcomes = await AnswerEval.RunAsync(questions, ciService, new Judge(azure.Judge), SearchMode.Hybrid);
    var current = EvalSummary.From(retrievalReport.Single(r => r.Mode == nameof(SearchMode.Hybrid)), answerOutcomes);

    var body = new StringBuilder();
    body.AppendLine("## RAG evaluation");
    body.AppendLine();
    body.AppendLine(Metrics.RenderMarkdown(retrievalReport, 10));
    body.AppendLine(AnswerEval.RenderMarkdown(answerOutcomes, SearchMode.Hybrid));

    if (argv.Contains("--update-baseline") || !File.Exists(baselinePath))
    {
        await File.WriteAllTextAsync(baselinePath, JsonSerializer.Serialize(current, jsonOptions));
        body.AppendLine($"Baseline written to {baselinePath}.");
        Console.WriteLine(body);
        return 0;
    }

    var baseline = JsonSerializer.Deserialize<EvalSummary>(await File.ReadAllTextAsync(baselinePath))!;
    var (table, passed) = Baseline.Compare(baseline, current);
    body.AppendLine("### Against baseline");
    body.AppendLine();
    body.AppendLine(table);
    body.AppendLine(passed ? "No regression beyond tolerance." : "**Regression detected.**");

    Console.WriteLine(body);
    if (Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is { Length: > 0 } summaryPath)
        await File.AppendAllTextAsync(summaryPath, body.ToString());
    return passed ? 0 : 1;
}

var k = int.Parse(Opt("--k") ?? "10");
var report = await RunRetrievalAsync(k, Enum.GetValues<SearchMode>());

var markdown = Metrics.RenderMarkdown(report, k);
Console.WriteLine();
Console.WriteLine(markdown);

var outDir = Opt("--out") ?? Path.Combine("eval", "results");
Directory.CreateDirectory(outDir);
var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
await File.WriteAllTextAsync(Path.Combine(outDir, $"retrieval-{stamp}.json"),
    JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
await File.WriteAllTextAsync(Path.Combine(outDir, $"retrieval-{stamp}.md"), markdown, new UTF8Encoding(false));
return 0;
