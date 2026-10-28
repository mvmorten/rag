using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Rag.Core.Chunking;

public sealed record ChunkerOptions(int MaxChars = 1800, int OverlapChars = 200, int MinChars = 80);

/// <summary>
/// Splits a Markdown document into chunks along heading boundaries. Oversized sections are split
/// on paragraph boundaries with overlap. Headings inside fenced code blocks are ignored.
/// </summary>
public sealed class MarkdownChunker(ChunkerOptions? options = null)
{
    private static readonly Regex Heading = new(@"^(#{1,6})\s+(.+?)\s*#*\s*$", RegexOptions.Compiled);
    private readonly ChunkerOptions _options = options ?? new ChunkerOptions();

    public IReadOnlyList<Chunk> Chunk(string sourcePath, string markdown)
    {
        var (meta, body) = FrontMatter.Parse(markdown);
        var title = meta.GetValueOrDefault("title") ?? Path.GetFileNameWithoutExtension(sourcePath);
        DateTimeOffset? updated = DateTimeOffset.TryParse(
            meta.GetValueOrDefault("ms.date"), null, System.Globalization.DateTimeStyles.AssumeUniversal, out var d) ? d : null;

        var chunks = new List<Chunk>();
        foreach (var (path, text) in Sections(title, body))
        {
            foreach (var piece in Split(text))
            {
                var ordinal = chunks.Count;
                chunks.Add(new Chunk(
                    Id: MakeId(sourcePath, ordinal),
                    SourcePath: sourcePath,
                    Title: title,
                    SectionPath: path,
                    Content: piece,
                    Ordinal: ordinal,
                    LastUpdated: updated,
                    ContentHash: Hash(piece)));
            }
        }
        return chunks;
    }

    private static IEnumerable<(string Path, string Text)> Sections(string title, string body)
    {
        var stack = new List<(int Level, string Text)>();
        var buffer = new StringBuilder();
        var inFence = false;
        var currentPath = title;

        foreach (var line in body.Replace("\r\n", "\n").Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("```") || trimmed.StartsWith("~~~")) inFence = !inFence;

            var m = inFence ? Match.Empty : Heading.Match(line);
            if (!m.Success) { buffer.AppendLine(line); continue; }

            var text = buffer.ToString().Trim();
            buffer.Clear();
            if (text.Length > 0) yield return (currentPath, text);

            var level = m.Groups[1].Length;
            stack.RemoveAll(h => h.Level >= level);
            stack.Add((level, m.Groups[2].Value));
            // A top-level H1 usually repeats the title, so don't duplicate it in the path.
            currentPath = string.Join(" > ", new[] { title }.Concat(
                stack.Select(h => h.Text).Where(t => !t.Equals(title, StringComparison.OrdinalIgnoreCase))));
        }

        var last = buffer.ToString().Trim();
        if (last.Length > 0) yield return (currentPath, last);
    }

    private IEnumerable<string> Split(string text)
    {
        if (text.Length <= _options.MaxChars) { yield return text; yield break; }

        var paragraphs = text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        var current = new StringBuilder();
        var hasNewContent = false;
        foreach (var p in paragraphs.SelectMany(HardWrap))
        {
            if (current.Length > 0 && current.Length + p.Length + 2 > _options.MaxChars)
            {
                var done = current.ToString().Trim();
                yield return done;
                current.Clear();
                var tail = Tail(done);
                if (tail.Length > 0) current.Append(tail).Append("\n\n");
                hasNewContent = false;
            }
            current.Append(p).Append("\n\n");
            hasNewContent = true;
        }

        // Skip a final fragment that is only overlap carried from the previous chunk.
        if (hasNewContent) yield return current.ToString().Trim();
    }

    private IEnumerable<string> HardWrap(string paragraph)
    {
        for (var i = 0; i < paragraph.Length; i += _options.MaxChars)
            yield return paragraph.Substring(i, Math.Min(_options.MaxChars, paragraph.Length - i));
    }

    private string Tail(string text)
    {
        if (_options.OverlapChars <= 0 || text.Length <= _options.OverlapChars) return "";
        var tail = text[^_options.OverlapChars..];
        var space = tail.IndexOf(' ');
        return space >= 0 ? tail[(space + 1)..] : tail;
    }

    private static string MakeId(string sourcePath, int ordinal) =>
        Hash($"{sourcePath.Replace('\\', '/')}#{ordinal}")[..24];

    public static string Hash(string s) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
}
