namespace Rag.Core.Chunking;

internal static class FrontMatter
{
    /// <summary>Parses simple `key: value` YAML front matter. Nested values are ignored.</summary>
    public static (Dictionary<string, string> Meta, string Body) Parse(string markdown)
    {
        var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var text = markdown.Replace("\r\n", "\n").TrimStart('﻿');
        if (!text.StartsWith("---\n")) return (meta, text);

        var end = text.IndexOf("\n---", 4, StringComparison.Ordinal);
        if (end < 0) return (meta, text);

        foreach (var line in text[4..end].Split('\n'))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0 || char.IsWhiteSpace(line[0])) continue;
            meta[line[..colon].Trim()] = line[(colon + 1)..].Trim().Trim('"', '\'');
        }
        var rest = text.IndexOf('\n', end + 1);
        return (meta, rest < 0 ? "" : text[(rest + 1)..]);
    }
}
