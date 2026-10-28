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

var k = int.Parse(Opt("--k") ?? "10");
var report = new List<ModeReport>();
foreach (var mode in Enum.GetValues<SearchMode>())
{
    var outcomes = new List<QuestionOutcome>();
    foreach (var q in questions.Where(q => q.Answerable))
    {
        var results = await retriever.SearchAsync(q.Question, mode, k);
        var urls = results.Select(r => r.Url).ToList();
        outcomes.Add(new QuestionOutcome(q.Id, Metrics.FirstRelevantRank(urls, q.Expected!), urls));
    }
    report.Add(new ModeReport(mode.ToString(), outcomes));
    Console.WriteLine($"{mode}: done");
}

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
