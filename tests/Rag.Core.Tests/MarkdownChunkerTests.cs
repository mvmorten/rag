using Rag.Core.Chunking;

namespace Rag.Core.Tests;

public class MarkdownChunkerTests
{
    private const string Fence = "```";

    private static readonly string Doc = string.Join("\n",
        "---",
        "title: Scale apps in Azure Container Apps",
        "ms.date: 03/15/2025",
        "---",
        "# Scale apps in Azure Container Apps",
        "",
        "Intro paragraph that is long enough to survive as its own chunk of text.",
        "",
        "## HTTP scaling",
        "",
        "Scale on concurrent HTTP requests.",
        "",
        "### Rules",
        "",
        "Set concurrentRequests per replica.",
        "",
        Fence + "bash",
        "# not a heading",
        "az containerapp update",
        Fence,
        "",
        "## Queue scaling",
        "",
        "Scale on queue length.");

    [Fact]
    public void Splits_on_headings_and_builds_section_paths()
    {
        var chunks = new MarkdownChunker().Chunk("container-apps/scale.md", Doc);

        Assert.Equal(
            new[]
            {
                "Scale apps in Azure Container Apps",
                "Scale apps in Azure Container Apps > HTTP scaling",
                "Scale apps in Azure Container Apps > HTTP scaling > Rules",
                "Scale apps in Azure Container Apps > Queue scaling",
            },
            chunks.Select(c => c.SectionPath));
    }

    [Fact]
    public void Ignores_headings_inside_code_fences()
    {
        var chunks = new MarkdownChunker().Chunk("a.md", Doc);

        Assert.Contains("# not a heading", chunks.Single(c => c.SectionPath.EndsWith("Rules")).Content);
        Assert.DoesNotContain(chunks, c => c.SectionPath.Contains("not a heading"));
    }

    [Fact]
    public void Reads_front_matter()
    {
        var chunks = new MarkdownChunker().Chunk("a.md", Doc);

        Assert.All(chunks, c => Assert.Equal("Scale apps in Azure Container Apps", c.Title));
        Assert.All(chunks, c => Assert.Equal(new DateTimeOffset(2025, 3, 15, 0, 0, 0, TimeSpan.Zero), c.LastUpdated));
    }

    [Fact]
    public void Falls_back_to_file_name_without_front_matter()
    {
        var chunks = new MarkdownChunker().Chunk("docs/overview.md", "## Part\n\nSome text.");

        Assert.Equal("overview", chunks[0].Title);
        Assert.Null(chunks[0].LastUpdated);
    }

    [Fact]
    public void Splits_oversized_sections_with_overlap()
    {
        var paragraphs = Enumerable.Range(0, 20).Select(i => $"Paragraph {i}: " + new string('x', 150));
        var md = "## Big\n\n" + string.Join("\n\n", paragraphs);

        var chunks = new MarkdownChunker(new ChunkerOptions(MaxChars: 600, OverlapChars: 100)).Chunk("big.md", md);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.True(c.Content.Length <= 600 + 100 + 4));
        Assert.All(chunks, c => Assert.EndsWith("> Large section", c.SectionPath));
        Assert.Equal(chunks.Count, chunks.Select(c => c.Id).Distinct().Count());
    }

    [Fact]
    public void Ids_are_stable_and_hash_tracks_content()
    {
        var chunker = new MarkdownChunker();
        var a = chunker.Chunk("a.md", "## S\n\nHello");
        var b = chunker.Chunk("a.md", "## S\n\nHello");
        var c = chunker.Chunk("a.md", "## S\n\nHello!");

        Assert.Equal(a[0].Id, b[0].Id);
        Assert.Equal(a[0].ContentHash, b[0].ContentHash);
        Assert.Equal(a[0].Id, c[0].Id);
        Assert.NotEqual(a[0].ContentHash, c[0].ContentHash);
    }
}
